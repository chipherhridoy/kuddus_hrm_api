using System;
using System.Collections.Generic;

namespace AgenticHrmApi.Contracts;

public class FaceLoginRequest
{
    public Guid ChallengeId { get; set; }
    public List<LivenessStep> Steps { get; set; } = new();
    public string FrontalBase64 { get; set; } = string.Empty;
}
