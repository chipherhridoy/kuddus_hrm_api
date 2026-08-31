namespace AgenticHrmApi.Contracts;

public class PendingAction
{
    // "applyLeave" | "approveLeave" | "rejectLeave" | "collectingSlots"
    public string Kind { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public int? LeaveId { get; set; }
    public Dictionary<string, string> Slots { get; set; } = new();
    public DateTime IssuedAt { get; set; }
    public int Attempts { get; set; }
}
