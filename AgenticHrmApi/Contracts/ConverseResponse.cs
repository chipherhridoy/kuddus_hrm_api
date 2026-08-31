namespace AgenticHrmApi.Contracts;

public class ConverseResponse
{
    public string Reply { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public string Intent { get; set; } = "chat";
    public bool ConversationOpen { get; set; }
    public bool DidAct { get; set; }
    public PendingAction? PendingAction { get; set; }
}
