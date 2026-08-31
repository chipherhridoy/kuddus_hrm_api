using System;
using System.Collections.Generic;
using System.Linq;
using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services.Face;

public class LivenessVerifier
{
    private readonly IFaceEngine _faceEngine;
    private readonly ILogger<LivenessVerifier> _logger;

    public LivenessVerifier(IFaceEngine faceEngine, ILogger<LivenessVerifier> logger)
    {
        _faceEngine = faceEngine;
        _logger = logger;
    }

    /// Client-supplied base64 is untrusted input; a malformed crop must fail the
    /// challenge cleanly rather than throw out of the verifier.
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

    public (bool Passed, string FailureDetail, List<float[]> Embeddings) Verify(FaceLoginRequest request, Models.FaceChallenge challenge)
    {
        if (challenge.Actions == null) return (false, "INVALID_CHALLENGE", new());

        var expectedActions = challenge.Actions.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        if (request.Steps.Count != expectedActions.Length)
        {
            return (false, "STEP_COUNT_MISMATCH", new());
        }

        long lastTimestamp = 0;
        var embeddings = new List<float[]>();

        for (int i = 0; i < expectedActions.Length; i++)
        {
            var step = request.Steps[i];
            var expectedAction = expectedActions[i];

            // 2. Actions arrive in the server's order
            if (!string.Equals(step.Action, expectedAction, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"ACTION_MISMATCH_STEP_{i}", new());
            }

            // G-3: Check Evidence for blink and smile
            if (expectedAction == "blink" && step.Evidence > FaceTuning.BlinkOpenMax)
            {
                return (false, $"BLINK_FAILED_STEP_{i}", new());
            }
            if (expectedAction == "smile" && step.Evidence < FaceTuning.SmileMin)
            {
                return (false, $"SMILE_FAILED_STEP_{i}", new());
            }

            // 6. Timing is plausible
            if (i > 0 && step.TimestampMs <= lastTimestamp)
            {
                return (false, $"TIMING_NON_INCREASING_STEP_{i}", new());
            }
            lastTimestamp = step.TimestampMs;

            // 3. Head yaw on turn_left / turn_right
            if (!TryDecodeBase64(step.CropBase64, out var imgBytes))
            {
                return (false, $"BAD_BASE64_STEP_{i}", new());
            }
            var face = _faceEngine.DetectLargest(imgBytes);
            
            if (face == null)
            {
                return (false, $"NO_FACE_IN_STEP_{i}", new());
            }

            if (expectedAction == "turn_left" || expectedAction == "turn_right")
            {
                var eyeMidX = (face.Value.LeftEyeX + face.Value.RightEyeX) / 2;
                var interOcular = Math.Abs(face.Value.LeftEyeX - face.Value.RightEyeX);
                
                if (interOcular <= 1) return (false, $"EYES_TOO_CLOSE_STEP_{i}", new());

                var yawRatio = (face.Value.NoseX - eyeMidX) / interOcular;
                _logger.LogInformation(
                    "Liveness step {Step}: action={Action} yawRatio={YawRatio:F3}",
                    i, expectedAction, yawRatio);

                var wanted = expectedAction == "turn_left" ? 1 : -1;
                var passed = wanted * yawRatio >= FaceTuning.YawRatioMin;

                if (!passed)
                {
                    // A turn that clears the threshold in the OPPOSITE direction is not a
                    // user who under-rotated: it means this sign convention disagrees with
                    // the device's. That mismatch would otherwise fail every single login
                    // with nothing in the logs to distinguish it from a shy user, so it is
                    // called out by name. See G-2 in the plan's verification log.
                    var inverted = wanted * yawRatio <= -FaceTuning.YawRatioMin;
                    var side = expectedAction == "turn_left" ? "LEFT" : "RIGHT";
                    var detail = inverted
                        ? $"YAW_SIGN_INVERTED_STEP_{i}_action={expectedAction}_ratio={yawRatio:F3}"
                        : $"YAW_{side}_FAILED_STEP_{i}_ratio={yawRatio:F3}";

                    if (inverted)
                    {
                        _logger.LogError(
                            "Yaw sign convention mismatch on '{Action}': measured ratio {YawRatio:F3} " +
                            "clears the threshold in the opposite direction. The server's " +
                            "FaceTuning.YawRatioMin convention and the client's yaw sign disagree; " +
                            "every face login will fail here until one is corrected.",
                            expectedAction, yawRatio);
                    }

                    return (false, detail, new());
                }
            }

            var emb = _faceEngine.Embed(imgBytes);
            if (emb == null) return (false, $"EMBED_FAILED_STEP_{i}", new());
            
            embeddings.Add(emb);
        }

        var totalTime = request.Steps.Last().TimestampMs - request.Steps.First().TimestampMs;
        if (totalTime < FaceTuning.MinChallengeMs || totalTime > FaceTuning.MaxChallengeMs)
        {
            return (false, "TIMING_OUT_OF_BOUNDS", new());
        }

        // 4. The step crops are genuinely different frames
        for (int i = 0; i < embeddings.Count; i++)
        {
            for (int j = i + 1; j < embeddings.Count; j++)
            {
                var sim = FaceMatcher.CosineSimilarity(embeddings[i], embeddings[j]);
                if (sim >= FaceTuning.MaxSelfSimilarity)
                {
                    return (false, $"CROPS_TOO_SIMILAR_{i}_{j}", new());
                }
            }
        }

        return (true, "", embeddings);
    }
}
