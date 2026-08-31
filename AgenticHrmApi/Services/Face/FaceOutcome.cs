namespace AgenticHrmApi.Services.Face;

public static class FaceOutcome
{
    public const string Success = "Success";
    public const string NoMatch = "NoMatch";
    public const string AmbiguousMatch = "AmbiguousMatch";
    public const string LivenessFailed = "LivenessFailed";
    public const string SpoofSuspected = "SpoofSuspected";
    public const string ChallengeExpired = "ChallengeExpired";
    public const string ChallengeReused = "ChallengeReused";
    public const string NoFaceDetected = "NoFaceDetected";
    public const string ServerError = "ServerError";
}
