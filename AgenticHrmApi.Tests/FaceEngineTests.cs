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
