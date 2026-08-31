using System;

namespace AgenticHrmApi.Models;

public class FaceLoginAttempt
{
    public long Id { get; set; }
    public int? MatchedUserId { get; set; }        // null on failure
    public string Outcome { get; set; } = "";      // see FaceOutcome constants
    public float BestScore { get; set; }
    public string ChallengeActions { get; set; } = "";  // "smile,turn_left,blink"
    public string FailureDetail { get; set; } = "";     // short code — NEVER an exception string
    public DateTime CreatedAt { get; set; }
}
