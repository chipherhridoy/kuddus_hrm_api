using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgenticHrmApi.Contracts;
using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Services.Face;

public class FaceEnrollmentService
{
    /// The poses an enrolment must cover. Order does not matter; presence does.
    private static readonly string[] RequiredPoses =
        ["frontal", "yaw_left", "yaw_right", "up", "down"];

    private readonly AppDbContext _db;
    private readonly IFaceEngine _faceEngine;
    private readonly TemplateCipher _cipher;

    /// Client-supplied base64 is untrusted input. Convert.FromBase64String throws
    /// FormatException on malformed data, which surfaces as a 500 with a stack
    /// trace rather than a bad request.
    private static bool TryDecodeBase64(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public FaceEnrollmentService(AppDbContext db, IFaceEngine faceEngine, TemplateCipher cipher)
    {
        _db = db;
        _faceEngine = faceEngine;
        _cipher = cipher;
    }

    public async Task<(bool Success, string ErrorMessage, int TemplatesStored)> EnrollUserAsync(int targetUserId, int adminUserId, FaceEnrollRequest request)
    {
        if (request.Captures.Count != FaceTuning.EnrollCaptureCount)
        {
            return (false, $"Expected {FaceTuning.EnrollCaptureCount} captures, got {request.Captures.Count}", 0);
        }

        // Check the subject exists before doing any work or opening a transaction.
        var user = await _db.Users.FindAsync(targetUserId);
        if (user == null) return (false, "User not found", 0);

        // The five poses exist so the stored templates span head angles; the matcher
        // takes a user's best template, so five frontal shots would collapse that
        // spread without failing any check below.
        var missing = RequiredPoses.Except(request.Captures.Select(c => c.Pose), StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0)
        {
            return (false, $"Missing required pose(s): {string.Join(", ", missing)}.", 0);
        }

        var embeddings = new List<(string Pose, float[] Emb, float Score)>();

        for (int i = 0; i < request.Captures.Count; i++)
        {
            var capture = request.Captures[i];

            if (!TryDecodeBase64(capture.CropBase64, out var imgBytes))
            {
                return (false, $"Pose {capture.Pose} is not valid base64 image data.", 0);
            }

            var face = _faceEngine.DetectLargest(imgBytes);
            if (face == null) return (false, $"Pose {capture.Pose} failed detection.", 0);

            var emb = _faceEngine.Embed(imgBytes);
            if (emb == null) return (false, $"Pose {capture.Pose} failed embedding.", 0);

            embeddings.Add((capture.Pose, emb, face.Value.Score));
        }

        // 3. Pairwise cosine >= EnrollConsistencyMin
        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = i + 1; j < embeddings.Count; j++)
            {
                var sim = FaceMatcher.CosineSimilarity(embeddings[i].Emb, embeddings[j].Emb);
                if (sim < FaceTuning.EnrollConsistencyMin)
                {
                    return (false, $"Captures {embeddings[i].Pose} and {embeddings[j].Pose} appear to be different people. Re-scan.", 0);
                }
                if (sim >= FaceTuning.MaxSelfSimilarity)
                {
                    return (false, $"Captures {embeddings[i].Pose} and {embeddings[j].Pose} are the same frame. Move between poses.", 0);
                }
            }
        }

        // 4. Duplicate check against existing users
        var frontal = embeddings.FirstOrDefault(e => e.Pose == "frontal").Emb ?? embeddings[0].Emb;
        var activeTemplates = await _db.FaceTemplates
            .Where(t => t.IsActive && t.UserId != targetUserId)
            .ToListAsync();

        var parsedTemplates = activeTemplates.Select(t => (t.UserId, _cipher.Decrypt(t.EncryptedEmbedding, t.Nonce, t.Tag))).ToList();
        
        var match = FaceMatcher.BestMatch(frontal, parsedTemplates);
        if (match.Outcome == FaceOutcome.Success || match.Outcome == FaceOutcome.AmbiguousMatch)
        {
            return (false, "This face is already enrolled for another user.", 0);
        }

        // 5. Transaction
        using var tx = await _db.Database.BeginTransactionAsync();
        
        var existing = await _db.FaceTemplates.Where(t => t.UserId == targetUserId && t.IsActive).ToListAsync();
        foreach (var ex in existing) ex.IsActive = false;

        user.FaceEnrolledAt = DateTime.UtcNow;

        foreach (var e in embeddings)
        {
            _cipher.Encrypt(e.Emb, out var cipherText, out var nonce, out var tag);
            _db.FaceTemplates.Add(new FaceTemplate
            {
                UserId = targetUserId,
                EncryptedEmbedding = cipherText,
                Nonce = nonce,
                Tag = tag,
                ModelVersion = "sface-2021dec",
                Pose = e.Pose,
                Quality = e.Score,
                EnrolledByUserId = adminUserId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return (true, "", embeddings.Count);
    }
}
