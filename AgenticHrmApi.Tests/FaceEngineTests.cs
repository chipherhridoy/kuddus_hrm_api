using System;
using System.Security.Cryptography;
using AgenticHrmApi.Services.Face;
using Xunit;

namespace AgenticHrmApi.Tests;

public class FaceEngineTests
{
    [Fact]
    public void Matcher_EmptyTemplateSet_ReturnsNoMatch()
    {
        var probe = new float[128];
        var result = FaceMatcher.BestMatch(probe, new List<(int, float[])>());
        Assert.Equal("NoMatch", result.Outcome);
        Assert.Null(result.UserId);
    }

    [Fact]
    public void Matcher_SamePersonMatches()
    {
        var probe = new float[128];
        probe[0] = 1f; // Mag = 1

        var t = new float[128];
        t[0] = 0.9f; 
        t[1] = 0.4358f; // Mag = ~1
        // Cosine sim = 0.9 > 0.363 (MatchThreshold)

        var result = FaceMatcher.BestMatch(probe, new List<(int, float[])> { (1, t) });
        Assert.Equal("Success", result.Outcome);
        Assert.Equal(1, result.UserId);
    }

    [Fact]
    public void Matcher_DifferentPeopleDoNotMatch()
    {
        var probe = new float[128];
        probe[0] = 1f; 

        var t = new float[128];
        t[1] = 1f; // Orthogonal, Cosine sim = 0

        var result = FaceMatcher.BestMatch(probe, new List<(int, float[])> { (1, t) });
        Assert.Equal("NoMatch", result.Outcome);
        Assert.Null(result.UserId);
    }

    [Fact]
    public void Matcher_TwoWayNearTie_ReturnsAmbiguousMatch()
    {
        var probe = new float[128];
        probe[0] = 1f;

        var t1 = new float[128];
        t1[0] = 0.9f;
        t1[1] = 0.4358f; 

        var t2 = new float[128];
        t2[0] = 0.88f; 
        t2[2] = 0.4749f; 
        
        // Both match, but margin (0.9 - 0.88 = 0.02) < 0.05
        var result = FaceMatcher.BestMatch(probe, new List<(int, float[])> { (1, t1), (2, t2) });
        Assert.Equal("AmbiguousMatch", result.Outcome);
        Assert.Null(result.UserId);
    }

    [Fact]
    public void Matcher_WithPreferredPose_PrioritizesMatchingPose()
    {
        var probe = new float[128];
        probe[0] = 1f;

        // User 1 has a frontal template with 0.95 similarity and a yaw_left template with 0.4 similarity
        var tFrontal = new float[128];
        tFrontal[0] = 0.95f;
        tFrontal[1] = 0.3122f;

        var tYawLeft = new float[128];
        tYawLeft[0] = 0.4f;
        tYawLeft[1] = 0.9165f;

        var templates = new List<(int UserId, string Pose, float[] Vec)>
        {
            (1, "frontal", tFrontal),
            (1, "yaw_left", tYawLeft),
        };

        var resultFrontal = FaceMatcher.BestMatch(probe, templates, preferredPose: "frontal");
        Assert.Equal("Success", resultFrontal.Outcome);
        Assert.Equal(1, resultFrontal.UserId);
        Assert.True(resultFrontal.Score > 0.9f);

        // If matching against yaw_left probe, yaw_left template is evaluated
        var leftProbe = new float[128];
        leftProbe[0] = 0.4f;
        leftProbe[1] = 0.9165f;
        var resultYawLeft = FaceMatcher.BestMatch(leftProbe, templates, preferredPose: "yaw_left");
        Assert.Equal("Success", resultYawLeft.Outcome);
        Assert.Equal(1, resultYawLeft.UserId);
        Assert.True(resultYawLeft.Score > 0.9f);
    }

    [Fact]
    public void Matcher_WithPreferredPose_FallsBackIfPoseNotPresent()
    {
        var probe = new float[128];
        probe[0] = 1f;

        var tFrontal = new float[128];
        tFrontal[0] = 0.9f;
        tFrontal[1] = 0.4358f;

        var templates = new List<(int UserId, string Pose, float[] Vec)>
        {
            (1, "frontal", tFrontal)
        };

        // Asks for "yaw_right" which doesn't exist; falls back gracefully to existing templates
        var result = FaceMatcher.BestMatch(probe, templates, preferredPose: "yaw_right");
        Assert.Equal("Success", result.Outcome);
        Assert.Equal(1, result.UserId);
    }

    [Fact]
    public void Cipher_RoundTrips()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var cipher = new TemplateCipher(Convert.ToBase64String(key));

        var original = new float[128];
        original[0] = 3.14159f;
        original[127] = -1.234f;

        cipher.Encrypt(original, out var ciphertext, out var nonce, out var tag);
        var decrypted = cipher.Decrypt(ciphertext, nonce, tag);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Cipher_TamperedTagThrows()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var cipher = new TemplateCipher(Convert.ToBase64String(key));

        var original = new float[128];
        cipher.Encrypt(original, out var ciphertext, out var nonce, out var tag);

        // Tamper
        tag[0] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(ciphertext, nonce, tag));
    }
}
