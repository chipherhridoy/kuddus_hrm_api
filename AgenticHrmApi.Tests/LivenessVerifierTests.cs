using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AgenticHrmApi.Contracts;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services.Face;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgenticHrmApi.Tests;

public class LivenessVerifierTests
{
    private class FakeLogger : ILogger<LivenessVerifier>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private LivenessVerifier CreateVerifier(Dictionary<string, float[]>? embeddings = null, Dictionary<string, DetectedFace>? detections = null)
    {
        var engine = new FakeFaceEngine(embeddings, detections);
        return new LivenessVerifier(engine, new FakeLogger());
    }

    private LivenessStep CreateStep(string action, long timestampMs, string contentKey, float evidence = 0.8f)
    {
        return new LivenessStep
        {
            Action = action,
            TimestampMs = timestampMs,
            Evidence = evidence,
            CropBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(contentKey))
        };
    }

    [Fact]
    public void Verify_ValidSequence_Passes()
    {
        var img2Array = new float[128];
        img2Array[0] = 1.0f;
        var embeddings = new Dictionary<string, float[]>
        {
            { "img1", new float[128] },
            { "img2", img2Array } // Ensure not exactly similar
        };
        var detections = new Dictionary<string, DetectedFace>
        {
            { "img1", new DetectedFace { LeftEyeX = 10, RightEyeX = 50, NoseX = 30 } }, // smile
            { "img2", new DetectedFace { LeftEyeX = 10, RightEyeX = 50, NoseX = 40 } } // turn_left
        };

        var verifier = CreateVerifier(embeddings, detections);

        var request = new FaceLoginRequest
        {
            ChallengeId = Guid.NewGuid(),
            Steps = new List<LivenessStep>
            {
                CreateStep("smile", 1000, "img1"),
                CreateStep("turn_left", 3000, "img2")
            }
        };

        var challenge = new FaceChallenge { Actions = "smile,turn_left" };

        var (passed, detail, embs) = verifier.Verify(request, challenge);

        Assert.True(passed, $"Expected pass but failed with: {detail}");
        Assert.Equal(2, embs.Count);
    }

    [Fact]
    public void Verify_ActionMismatch_Fails()
    {
        var verifier = CreateVerifier();
        var request = new FaceLoginRequest
        {
            Steps = new List<LivenessStep>
            {
                CreateStep("nod", 1000, "img1")
            }
        };
        var challenge = new FaceChallenge { Actions = "smile" };

        var (passed, detail, _) = verifier.Verify(request, challenge);

        Assert.False(passed);
        Assert.Equal("ACTION_MISMATCH_STEP_0", detail);
    }

    [Fact]
    public void Verify_NonIncreasingTimestamps_Fails()
    {
        var embeddings = new Dictionary<string, float[]>
        {
            { "img1", new float[128] },
            { "img2", new float[128] }
        };
        var detections = new Dictionary<string, DetectedFace>
        {
            { "img1", new DetectedFace() },
            { "img2", new DetectedFace() }
        };
        var verifier = CreateVerifier(embeddings, detections);
        var request = new FaceLoginRequest
        {
            Steps = new List<LivenessStep>
            {
                CreateStep("smile", 2000, "img1"),
                CreateStep("blink", 1000, "img2", 0.1f)
            }
        };
        var challenge = new FaceChallenge { Actions = "smile,blink" };

        var (passed, detail, _) = verifier.Verify(request, challenge);

        Assert.False(passed);
        Assert.Equal("TIMING_NON_INCREASING_STEP_1", detail);
    }

    [Fact]
    public void Verify_TimingOutOfBounds_Fails()
    {
        var img2Array = new float[128];
        img2Array[0] = 1.0f;
        var embeddings = new Dictionary<string, float[]>
        {
            { "img1", new float[128] },
            { "img2", img2Array }
        };
        var detections = new Dictionary<string, DetectedFace>
        {
            { "img1", new DetectedFace() },
            { "img2", new DetectedFace() }
        };

        var verifier = CreateVerifier(embeddings, detections);
        var request = new FaceLoginRequest
        {
            Steps = new List<LivenessStep>
            {
                CreateStep("smile", 1000, "img1"),
                CreateStep("blink", 35000, "img2", 0.1f) // Total time 34s > MaxChallengeMs (30s)
            }
        };
        var challenge = new FaceChallenge { Actions = "smile,blink" };

        var (passed, detail, _) = verifier.Verify(request, challenge);

        Assert.False(passed);
        Assert.Equal("TIMING_OUT_OF_BOUNDS", detail);
    }

    [Fact]
    public void Verify_NoFaceInStep_Fails()
    {
        var verifier = CreateVerifier(new Dictionary<string, float[]>(), new Dictionary<string, DetectedFace>()); // No detections mapped
        var request = new FaceLoginRequest
        {
            Steps = new List<LivenessStep>
            {
                CreateStep("smile", 1000, "img1")
            }
        };
        var challenge = new FaceChallenge { Actions = "smile" };

        var (passed, detail, _) = verifier.Verify(request, challenge);

        Assert.False(passed);
        Assert.Equal("NO_FACE_IN_STEP_0", detail);
    }

    [Fact]
    public void Verify_YawFailure_Fails()
    {
        var detections = new Dictionary<string, DetectedFace>
        {
            { "img1", new DetectedFace { LeftEyeX = 10, RightEyeX = 50, NoseX = 30 } } // Center facing, yawRatio = 0
        };
        var verifier = CreateVerifier(new Dictionary<string, float[]>(), detections);
        
        var request = new FaceLoginRequest
        {
            Steps = new List<LivenessStep>
            {
                CreateStep("turn_left", 1000, "img1")
            }
        };
        var challenge = new FaceChallenge { Actions = "turn_left" };

        var (passed, detail, _) = verifier.Verify(request, challenge);

        Assert.False(passed);
        // The measured ratio is appended for diagnosis, so match the prefix.
        Assert.StartsWith("YAW_LEFT_FAILED_STEP_0", detail);
        Assert.Contains("ratio=0.000", detail);
    }

    [Fact]
    public void Verify_YawTurnedTheWrongWay_ReportsSignInversion()
    {
        // Nose displaced hard toward the eye OPPOSITE the demanded direction. A user
        // who simply under-rotated lands near zero; this is a full turn the server
        // scores backwards, which is the signature of a client/server sign
        // disagreement rather than a shy user. See G-2.
        var detections = new Dictionary<string, DetectedFace>
        {
            { "img1", new DetectedFace { LeftEyeX = 10, RightEyeX = 50, NoseX = 10 } } // yawRatio = -0.5
        };
        var verifier = CreateVerifier(new Dictionary<string, float[]>(), detections);

        var request = new FaceLoginRequest
        {
            Steps = new List<LivenessStep> { CreateStep("turn_left", 1000, "img1") }
        };
        var challenge = new FaceChallenge { Actions = "turn_left" };

        var (passed, detail, _) = verifier.Verify(request, challenge);

        Assert.False(passed);
        Assert.StartsWith("YAW_SIGN_INVERTED_STEP_0", detail);
        Assert.Contains("ratio=-0.500", detail);
    }

    [Fact]
    public void Verify_BadBase64_FailsCleanly()
    {
        var verifier = CreateVerifier(new Dictionary<string, float[]>(), new Dictionary<string, DetectedFace>());

        var request = new FaceLoginRequest
        {
            Steps = new List<LivenessStep>
            {
                new() { Action = "blink", CropBase64 = "!!!not base64!!!", Evidence = 0.1f, TimestampMs = 1000 }
            }
        };
        var challenge = new FaceChallenge { Actions = "blink" };

        var (passed, detail, _) = verifier.Verify(request, challenge);

        Assert.False(passed);
        Assert.Equal("BAD_BASE64_STEP_0", detail);
    }

    [Fact]
    public void Verify_CropsTooSimilar_Fails()
    {
        var mockEmb = new float[128];
        mockEmb[0] = 1.0f;
        var embeddings = new Dictionary<string, float[]>
        {
            { "img1", mockEmb }, // Exactly the same, but not all zeros
            { "img2", mockEmb }
        };
        var detections = new Dictionary<string, DetectedFace>
        {
            { "img1", new DetectedFace() },
            { "img2", new DetectedFace() }
        };

        var verifier = CreateVerifier(embeddings, detections);
        var request = new FaceLoginRequest
        {
            Steps = new List<LivenessStep>
            {
                CreateStep("smile", 1000, "img1"),
                CreateStep("blink", 3000, "img2", 0.1f)
            }
        };
        var challenge = new FaceChallenge { Actions = "smile,blink" };

        var (passed, detail, _) = verifier.Verify(request, challenge);

        Assert.False(passed);
        Assert.Equal("CROPS_TOO_SIMILAR_0_1", detail);
    }
}
