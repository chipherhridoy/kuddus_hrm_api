using System;

namespace AgenticHrmApi.Models;

public class FaceChallenge
{
    public Guid Id { get; set; }
    public string Actions { get; set; } = "";   // ordered, comma-separated
    public DateTime ExpiresAt { get; set; }
    public bool Consumed { get; set; }
    public DateTime CreatedAt { get; set; }
}
