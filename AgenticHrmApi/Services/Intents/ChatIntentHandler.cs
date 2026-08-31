using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services.Intents;

public class ChatIntentHandler : IIntentHandler
{
    public const string HelpReply =
        "You can check in or out, apply for leave, or ask about your attendance.";

    public bool CanHandle(string intent) =>
        intent is "chat" or "chat.smalltalk" or "chat.help";

    public Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default)
    {
        // Gemini authors small talk; code only supplies the fallback and the
        // length cap applied in ConversationService (spec 9.4, 11).
        var reply = ctx.Intent == "chat.help" || string.IsNullOrWhiteSpace(ctx.ProposedReply)
            ? HelpReply
            : ctx.ProposedReply!;

        return Task.FromResult(HandlerResult.Open(reply));
    }
}
