using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgenticHrmApi.Contracts;
using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services.Face;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Xunit;

namespace AgenticHrmApi.Tests;

public class FaceEnrollmentServiceTests
{
    private const int AdminId = 1;
    private const int TargetId = 3;

    /// The service opens a real transaction; the in-memory provider raises that as an
    /// error unless the warning is suppressed.
    private static AppDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new AppDbContext(options);
        db.Users.AddRange(
            new User { Id = AdminId, Name = "Mahfuz Admin", Email = "admin@kuddus.com", PasswordHash = "x", Role = "Admin" },
            new User { Id = TargetId, Name = "Rahim Uddin", Email = "rahim@kuddus.com", PasswordHash = "x", Role = "Employee" },
            new User { Id = 4, Name = "Karim Ahmed", Email = "karim@kuddus.com", PasswordHash = "x", Role = "Employee" }
        );
        db.SaveChanges();
        return db;
    }

    /// Vectors that are clearly one person (pairwise cosine 0.8, above
    /// EnrollConsistencyMin) but never the identical frame (below MaxSelfSimilarity).
    private static float[] PoseVector(int i)
    {
        var v = new float[128];
        v[0] = 1f;
        v[i + 1] = 0.5f;
        return v;
    }

    private static float[] OtherPersonVector()
    {
        var v = new float[128];
        v[100] = 1f;
        return v;
    }

    private static string Key(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private static readonly string[] AllPoses = ["frontal", "yaw_left", "yaw_right", "up", "down"];

    private static (FaceEnrollmentService Svc, AppDbContext Db) CreateService(
        string dbName,
        IEnumerable<string> poses,
        Func<int, string> contentKeyFor,
        Dictionary<string, float[]>? extraEmbeddings = null)
    {
        var db = CreateDb(dbName);

        var embeddings = extraEmbeddings ?? new Dictionary<string, float[]>();
        var detections = new Dictionary<string, DetectedFace>();
        var poseList = poses.ToList();

        for (int i = 0; i < poseList.Count; i++)
        {
            var k = contentKeyFor(i);
            embeddings[k] = PoseVector(i);
            detections[k] = new DetectedFace { Score = 0.99f };
        }

        var cipher = new TemplateCipher(Convert.ToBase64String(new byte[32]));
        var svc = new FaceEnrollmentService(db, new FakeFaceEngine(embeddings, detections), cipher);
        return (svc, db);
    }

    private static FaceEnrollRequest Request(IEnumerable<string> poses, Func<int, string> contentKeyFor)
    {
        var list = poses.ToList();
        return new FaceEnrollRequest
        {
            UserId = TargetId,
            Captures = list.Select((p, i) => new FaceCapture { Pose = p, CropBase64 = Key(contentKeyFor(i)) }).ToList()
        };
    }

    [Fact]
    public async Task Enroll_HappyPath_StoresFiveTemplatesAndStampsUser()
    {
        var (svc, db) = CreateService(nameof(Enroll_HappyPath_StoresFiveTemplatesAndStampsUser), AllPoses, i => $"p{i}");

        var (success, error, count) = await svc.EnrollUserAsync(TargetId, AdminId, Request(AllPoses, i => $"p{i}"));

        Assert.True(success, error);
        Assert.Equal(5, count);
        Assert.Equal(5, db.FaceTemplates.Count(t => t.UserId == TargetId && t.IsActive));
        Assert.NotNull(db.Users.Find(TargetId)!.FaceEnrolledAt);
        // The enrolling admin is recorded, and it comes from the caller, not the body.
        Assert.All(db.FaceTemplates.ToList(), t => Assert.Equal(AdminId, t.EnrolledByUserId));
    }

    [Fact]
    public async Task Enroll_StoresCiphertext_NotRawEmbedding()
    {
        var (svc, db) = CreateService(nameof(Enroll_StoresCiphertext_NotRawEmbedding), AllPoses, i => $"p{i}");

        await svc.EnrollUserAsync(TargetId, AdminId, Request(AllPoses, i => $"p{i}"));

        var stored = db.FaceTemplates.First();
        var plaintext = new byte[PoseVector(0).Length * sizeof(float)];
        Buffer.BlockCopy(PoseVector(0), 0, plaintext, 0, plaintext.Length);
        Assert.NotEqual(plaintext, stored.EncryptedEmbedding);
        Assert.Equal(12, stored.Nonce.Length);
        Assert.Equal(16, stored.Tag.Length);
    }

    [Fact]
    public async Task Enroll_MissingRequiredPose_IsRejected()
    {
        // Five captures, but "down" is replaced by a second "frontal".
        string[] poses = ["frontal", "yaw_left", "yaw_right", "up", "frontal"];
        var (svc, db) = CreateService(nameof(Enroll_MissingRequiredPose_IsRejected), poses, i => $"p{i}");

        var (success, error, _) = await svc.EnrollUserAsync(TargetId, AdminId, Request(poses, i => $"p{i}"));

        Assert.False(success);
        Assert.Contains("down", error);
        Assert.Empty(db.FaceTemplates.ToList());
    }

    [Fact]
    public async Task Enroll_SameFrameFiveTimes_IsRejected()
    {
        // Every capture carries the same image, so all five embed identically.
        // Consistency alone would pass this; the diversity bound is what catches it.
        var (svc, db) = CreateService(nameof(Enroll_SameFrameFiveTimes_IsRejected), AllPoses, _ => "same");

        var (success, error, _) = await svc.EnrollUserAsync(TargetId, AdminId, Request(AllPoses, _ => "same"));

        Assert.False(success);
        Assert.Contains("same frame", error);
        Assert.Empty(db.FaceTemplates.ToList());
    }

    [Fact]
    public async Task Enroll_MalformedBase64_FailsCleanlyWithoutThrowing()
    {
        var (svc, db) = CreateService(nameof(Enroll_MalformedBase64_FailsCleanlyWithoutThrowing), AllPoses, i => $"p{i}");

        var request = Request(AllPoses, i => $"p{i}");
        request.Captures[2].CropBase64 = "!!!not base64!!!";

        var (success, error, _) = await svc.EnrollUserAsync(TargetId, AdminId, request);

        Assert.False(success);
        Assert.Contains("base64", error);
        Assert.Empty(db.FaceTemplates.ToList());
    }

    [Fact]
    public async Task Enroll_WrongCaptureCount_IsRejected()
    {
        var (svc, _) = CreateService(nameof(Enroll_WrongCaptureCount_IsRejected), AllPoses, i => $"p{i}");

        var request = Request(AllPoses, i => $"p{i}");
        request.Captures.RemoveAt(0);

        var (success, error, _) = await svc.EnrollUserAsync(TargetId, AdminId, request);

        Assert.False(success);
        Assert.Contains("Expected", error);
    }

    [Fact]
    public async Task Enroll_UnknownUser_IsRejected()
    {
        var (svc, _) = CreateService(nameof(Enroll_UnknownUser_IsRejected), AllPoses, i => $"p{i}");

        var (success, error, _) = await svc.EnrollUserAsync(9999, AdminId, Request(AllPoses, i => $"p{i}"));

        Assert.False(success);
        Assert.Contains("not found", error);
    }

    [Fact]
    public async Task Enroll_FaceAlreadyBelongsToAnotherUser_IsRejected()
    {
        var dbName = nameof(Enroll_FaceAlreadyBelongsToAnotherUser_IsRejected);
        var (svc, db) = CreateService(dbName, AllPoses, i => $"p{i}");

        // User 4 is already enrolled with the very face now being offered for user 3.
        var cipher = new TemplateCipher(Convert.ToBase64String(new byte[32]));
        cipher.Encrypt(PoseVector(0), out var ct, out var nonce, out var tag);
        db.FaceTemplates.Add(new FaceTemplate
        {
            UserId = 4, EncryptedEmbedding = ct, Nonce = nonce, Tag = tag,
            Pose = "frontal", IsActive = true, EnrolledByUserId = AdminId, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var (success, error, _) = await svc.EnrollUserAsync(TargetId, AdminId, Request(AllPoses, i => $"p{i}"));

        Assert.False(success);
        Assert.Contains("already enrolled", error);
        Assert.Empty(db.FaceTemplates.Where(t => t.UserId == TargetId).ToList());
    }

    [Fact]
    public async Task Enroll_Again_DeactivatesThePreviousTemplates()
    {
        var dbName = nameof(Enroll_Again_DeactivatesThePreviousTemplates);
        var (svc, db) = CreateService(dbName, AllPoses, i => $"p{i}");

        await svc.EnrollUserAsync(TargetId, AdminId, Request(AllPoses, i => $"p{i}"));
        var firstIds = db.FaceTemplates.Where(t => t.UserId == TargetId).Select(t => t.Id).ToList();

        await svc.EnrollUserAsync(TargetId, AdminId, Request(AllPoses, i => $"p{i}"));

        Assert.All(db.FaceTemplates.Where(t => firstIds.Contains(t.Id)).ToList(), t => Assert.False(t.IsActive));
        Assert.Equal(5, db.FaceTemplates.Count(t => t.UserId == TargetId && t.IsActive));
    }
}
