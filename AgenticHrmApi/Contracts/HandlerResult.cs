namespace AgenticHrmApi.Contracts;

public class HandlerResult
{
    public required string Reply { get; init; }
    public bool ConversationOpen { get; init; }
    public bool DidAct { get; init; }
    public PendingAction? Pending { get; init; }

    /// Spoken length budget for this reply. HR replies state one fact and
    /// stay terse; open-domain answers need room for three sentences.
    public int MaxReplyChars { get; init; } = DefaultMaxReplyChars;

    public const int DefaultMaxReplyChars = 200;

    public static HandlerResult Open(
        string reply,
        PendingAction? pending = null,
        int maxReplyChars = DefaultMaxReplyChars) =>
        new()
        {
            Reply = reply,
            ConversationOpen = true,
            Pending = pending,
            MaxReplyChars = maxReplyChars
        };

    public static HandlerResult Closed(string reply) =>
        new() { Reply = reply, ConversationOpen = false };

    public static HandlerResult Acted(string reply) =>
        new() { Reply = reply, ConversationOpen = true, DidAct = true };
}
