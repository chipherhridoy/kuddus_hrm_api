using System;

namespace AgenticHrmApi.Contracts;

public class FaceChallengeResponse
{
    public Guid ChallengeId { get; set; }
    public string[] Actions { get; set; } = Array.Empty<string>();
    public DateTime ExpiresAt { get; set; }
}
