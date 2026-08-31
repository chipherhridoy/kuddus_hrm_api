namespace AgenticHrmApi.Services.Face;

public static class FaceTuning
{
    // Matching — starting points only. TUNE against your own captures and record why.
    public const float MatchThreshold      = 0.363f;  // OpenCV's published SFace cosine reference
    public const float IdentityMargin      = 0.05f;   // best must beat 2nd-best *different user* by this
    public const float EnrollConsistencyMin= 0.30f;   // the 5 enrol poses must agree at least this much
    public const float MaxSelfSimilarity   = 0.995f;  // liveness check #4: step crops must differ
    public const float MinDetectScore      = 0.85f;

    // Liveness
    public const int   ActionsPerChallenge = 3;
    public const int   ChallengeTtlSeconds = 30;
    public const int   MinChallengeMs      = 1200;
    public const int   MaxChallengeMs      = 30_000;
    public const float YawRatioMin         = 0.15f;
    public const float BlinkOpenMax        = 0.25f;
    public const float SmileMin            = 0.70f;

    // Enrolment
    public const int   EnrollCaptureCount  = 5;
    public const int   MaxTemplatesPerUser = 10;

    // Retention
    public const int   AttemptRetentionDays = 90;
}
