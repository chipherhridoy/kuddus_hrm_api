namespace AgenticHrmApi.Contracts;

public class HandlerResult
{
    public required string Reply { get; init; }
    public bool ConversationOpen { get; init; }
    public bool DidAct { get; init; }
    public PendingAction? Pending { get; init; }

    public static HandlerResult Open(string reply, PendingAction? pending = null) =>
        new() { Reply = reply, ConversationOpen = true, Pending = pending };

    public static HandlerResult Closed(string reply) =>
        new() { Reply = reply, ConversationOpen = false };

    public static HandlerResult Acted(string reply) =>
        new() { Reply = reply, ConversationOpen = true, DidAct = true };
}
