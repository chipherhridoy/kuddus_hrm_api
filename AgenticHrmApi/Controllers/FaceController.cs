using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AgenticHrmApi.Contracts;
using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services.Auth;
using AgenticHrmApi.Services.Face;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace AgenticHrmApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FaceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FaceEnrollmentService _enrollmentService;
    private readonly LivenessVerifier _livenessVerifier;
    private readonly IFaceEngine _faceEngine;
    private readonly TemplateCipher _cipher;
    private readonly JwtTokenService _jwt;
    private readonly ILogger<FaceController> _logger;

    public FaceController(AppDbContext db, FaceEnrollmentService enrollmentService, LivenessVerifier livenessVerifier, IFaceEngine faceEngine, TemplateCipher cipher, JwtTokenService jwt, ILogger<FaceController> logger)
    {
        _db = db;
        _enrollmentService = enrollmentService;
        _livenessVerifier = livenessVerifier;
        _faceEngine = faceEngine;
        _cipher = cipher;
        _jwt = jwt;
        _logger = logger;
    }

    [HttpPost("challenge")]
    [EnableRateLimiting("face")]
    public async Task<IActionResult> CreateChallenge()
    {
        try
        {
            var allActions = new[] { "blink", "smile", "turn_left", "turn_right", "nod" };
            var chosen = allActions.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).Take(FaceTuning.ActionsPerChallenge).ToArray();
            
            var challenge = new FaceChallenge
            {
                Id = Guid.NewGuid(),
                Actions = string.Join(",", chosen),
                ExpiresAt = DateTime.UtcNow.AddSeconds(FaceTuning.ChallengeTtlSeconds),
                Consumed = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.FaceChallenges.Add(challenge);
            await _db.SaveChangesAsync();

            return Ok(new FaceChallengeResponse
            {
                ChallengeId = challenge.Id,
                Actions = chosen,
                ExpiresAt = challenge.ExpiresAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception creating face challenge");
            return StatusCode(500, new { message = $"Failed to create face challenge: {ex.Message}" });
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting("face")]
    public async Task<IActionResult> Login([FromBody] FaceLoginRequest request)
    {
        var attempt = new FaceLoginAttempt
        {
            CreatedAt = DateTime.UtcNow,
            ChallengeActions = string.Join(",", request.Steps.Select(s => $"{s.Action}:{s.Evidence:F2}"))
        };

        try
        {
            // 1. Consume challenge
            var challenge = await _db.FaceChallenges.FindAsync(request.ChallengeId);
            if (challenge == null)
            {
                attempt.Outcome = FaceOutcome.ChallengeReused;
                attempt.FailureDetail = "NOT_FOUND";
                return Unauthorized(new { message = "Face not recognised" });
            }

            var rows = await _db.Database.ExecuteSqlRawAsync(
                "UPDATE \"FaceChallenges\" SET \"Consumed\"=true WHERE \"Id\"={0} AND \"Consumed\"=false",
                request.ChallengeId);

            if (rows != 1)
            {
                attempt.Outcome = FaceOutcome.ChallengeReused;
                attempt.FailureDetail = "CONSUMED";
                return Unauthorized(new { message = "Face not recognised" });
            }

            if (challenge.ExpiresAt < DateTime.UtcNow)
            {
                attempt.Outcome = FaceOutcome.ChallengeExpired;
                attempt.FailureDetail = "EXPIRED";
                return Unauthorized(new { message = "Face not recognised" });
            }

            var (passed, failReason, stepEmbeddings) = _livenessVerifier.Verify(request, challenge!);
            if (!passed)
            {
                attempt.Outcome = FaceOutcome.LivenessFailed;
                attempt.FailureDetail = failReason;
                return Unauthorized(new { message = "Face not recognised" });
            }

            // Frontal match
            var frontalBytes = Convert.FromBase64String(request.FrontalBase64);
            var frontalEmb = _faceEngine.Embed(frontalBytes);
            if (frontalEmb == null)
            {
                attempt.Outcome = FaceOutcome.NoFaceDetected;
                attempt.FailureDetail = "FRONTAL_EMBED_FAIL";
                return Unauthorized(new { message = "Face not recognised" });
            }

            var activeTemplates = await _db.FaceTemplates.Where(t => t.IsActive).ToListAsync();
            var parsedTemplates = activeTemplates.Select(t => (t.UserId, Pose: t.Pose, Vec: _cipher.Decrypt(t.EncryptedEmbedding, t.Nonce, t.Tag))).ToList();

            var match = FaceMatcher.BestMatch(frontalEmb, parsedTemplates, preferredPose: "frontal");
            attempt.BestScore = match.Score;

            if (match.Outcome != FaceOutcome.Success)
            {
                attempt.Outcome = match.Outcome;
                attempt.FailureDetail = "NO_MATCH";
                return Unauthorized(new { message = "Face not recognised" });
            }

            // Cross-check steps using corresponding pose templates
            for (int i = 0; i < request.Steps.Count && i < stepEmbeddings.Count; i++)
            {
                var step = request.Steps[i];
                var stepEmb = stepEmbeddings[i];
                string preferredPose = step.Action switch
                {
                    "turn_left" => "yaw_left",
                    "turn_right" => "yaw_right",
                    "nod" => "down",
                    _ => "frontal"
                };

                var userTemplates = parsedTemplates.Where(t => t.UserId == match.UserId).ToList();
                var stepMatch = FaceMatcher.BestMatch(stepEmb, userTemplates, preferredPose: preferredPose);
                if (stepMatch.Score < FaceTuning.MatchThreshold)
                {
                    attempt.Outcome = FaceOutcome.SpoofSuspected;
                    attempt.FailureDetail = $"STEP_IDENTITY_MISMATCH_{step.Action.ToUpper()}";
                    return Unauthorized(new { message = "Face not recognised" });
                }
            }

            attempt.Outcome = FaceOutcome.Success;
            attempt.MatchedUserId = match.UserId;

            var user = await _db.Users.FindAsync(match.UserId);
            var token = _jwt.CreateToken(user!);
            
            var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(token);

            return Ok(new
            {
                token,
                expiresAt = jwtToken.ValidTo,
                user = new
                {
                    id = user!.Id,
                    name = user.Name,
                    email = user.Email,
                    role = user.Role,
                    department = user.Department,
                    designation = user.Designation,
                    faceEnrolled = user.FaceEnrolledAt.HasValue
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during face login");
            attempt.Outcome = FaceOutcome.ServerError;
            attempt.FailureDetail = "EXCEPTION_THROWN";
            return Unauthorized(new { message = "Face not recognised" });
        }
        finally
        {
            _db.FaceLoginAttempts.Add(attempt);
            await _db.SaveChangesAsync();
        }
    }

    [HttpPost("enroll")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Enroll([FromBody] FaceEnrollRequest request)
    {
        try
        {
            int currentUserId = this.CurrentUserId();
            var (success, error, count) = await _enrollmentService.EnrollUserAsync(request.UserId, currentUserId, request);

            if (!success)
            {
                if (error == "This face is already enrolled for another user.") return Conflict(new { message = error });
                return BadRequest(new { message = error });
            }

            return Ok(new { userId = request.UserId, templatesStored = count, enrolledAt = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during face enrollment");
            return StatusCode(500, new { message = $"Server error during enrollment: {ex.Message}" });
        }
    }

    [HttpDelete("{userId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int userId)
    {
        var existing = await _db.FaceTemplates.Where(t => t.UserId == userId && t.IsActive).ToListAsync();
        foreach (var ex in existing) ex.IsActive = false;

        var user = await _db.Users.FindAsync(userId);
        if (user != null) user.FaceEnrolledAt = null;

        await _db.SaveChangesAsync();
        return Ok();
    }
}
