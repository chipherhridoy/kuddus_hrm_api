using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services.Intents;

/// Everything that is not an HR action lands here: small talk, "what can
/// you do", and — since the open-domain work — any real question.
///
/// [answerer] is optional because running without open-domain answering is
/// a legitimate production state (GroundedAnswersEnabled=false). With it
/// null, this handler behaves exactly as it did before that feature.
public class ChatIntentHandler(IAnswerer? answerer = null) : IIntentHandler
{
    public const string HelpReply =
        "You can check in or out, apply for leave, or ask about your attendance.";

    /// Three sentences of speech. HR replies stay at the 200-char default —
    /// they state one fact.
    public const int MaxAnswerChars = 350;

    public bool CanHandle(IntentContext ctx) =>
        ctx.Intent is "chat" or "chat.smalltalk" or "chat.help" or "chat.answer";

    public async Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default)
    {
        // "What can you do" is a question about this app, not about the
        // world. Never spend a grounded call on it.
        if (ctx.Intent == "chat.help")
            return HandlerResult.Open(HelpReply);

        if (answerer is not null && !string.IsNullOrWhiteSpace(ctx.Transcript))
        {
            var answer = await answerer.AnswerAsync(new AnswerInput
            {
                User = ctx.User,
                Utterance = ctx.Transcript,
                History = ctx.History,
                Today = DateTime.UtcNow.Date
            }, ct);

            if (!string.IsNullOrWhiteSpace(answer))
                return HandlerResult.Open(answer, null, MaxAnswerChars);
        }

        // Answerer absent or dead: exactly the pre-existing behaviour.
        // Gemini authors small talk; code only supplies the fallback.
        var reply = string.IsNullOrWhiteSpace(ctx.ProposedReply) ? HelpReply : ctx.ProposedReply!;
        return HandlerResult.Open(reply);
    }
}
