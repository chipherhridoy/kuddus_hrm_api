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
    private readonly AppDbContext _db;
    private readonly TemplateCipher _cipher;

    public FaceEnrollmentService(AppDbContext db, TemplateCipher cipher)
    {
        _db = db;
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

        var embeddings = new List<(string Pose, float[] Emb, float Score)>();

        for (int i = 0; i < request.Captures.Count; i++)
        {
            var capture = request.Captures[i];

            if (capture.Embedding == null || capture.Embedding.Length == 0)
            {
                return (false, $"Pose {capture.Pose} is missing embedding data.", 0);
            }

            embeddings.Add((capture.Pose, capture.Embedding, 1.0f)); // Quality is assumed 1.0 for on-device capture
        }

        // Duplicate check against existing users
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

        // Transaction
        using var tx = await _db.Database.BeginTransactionAsync();
        
        var existing = await _db.FaceTemplates.Where(t => t.UserId == targetUserId && t.IsActive).ToListAsync();
        foreach (var ex in existing) 
        {
            ex.IsActive = false;
            ex.UpdatedAt = DateTime.UtcNow; // Mark for sync deletion
        }

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
                ModelVersion = "mobilefacenet-tflite",
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
