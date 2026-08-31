using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services.Intents;

public class ControlIntentHandler(
    LeaveIntentHandler leave,
    ManagerIntentHandler manager) : IIntentHandler
{
    public const int MaxConfirmationReasks = 1;

    public bool CanHandle(string intent) => intent.StartsWith("control.");

    public async Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default)
    {
        if (ctx.Pending is null)
            return HandlerResult.Open("Sorry, what would you like me to do?");

        // Spec 6.7: a cut-off read-back was never fully heard, so "yes" is not consent.
        if (ctx.History.Any(t => t.Role == "kuddus" && t.Truncated))
            return HandlerResult.Open(
                "Sorry, I cut out — let me repeat that.",
                Retry(ctx.Pending, ctx.Pending.Attempts));

        var kind = AnswerClassifier.Classify(ctx.Transcript);

        switch (kind)
        {
            case AnswerKind.Affirmative:
                return ctx.Pending.Kind switch
                {
                    "applyLeave" => await leave.CommitAsync(ctx, ct),
                    "approveLeave" or "rejectLeave" => await manager.CommitAsync(ctx, ct),
                    _ => HandlerResult.Open("I've lost the thread — ask me again.")
                };

            case AnswerKind.Negative:
                return HandlerResult.Open("Okay, cancelled. Anything else?");

            case AnswerKind.Cancelling:
                return HandlerResult.Closed("Okay, never mind.");

            case AnswerKind.Correction:
                // Re-run the originating handler with the corrected slots.
                return HandlerResult.Open("Let me redo that.", Retry(ctx.Pending, 0));

            default:
                var attempts = ctx.Pending.Attempts + 1;
                if (attempts > MaxConfirmationReasks)
                    return HandlerResult.Closed("I'll leave that for now — nothing was submitted.");

                return HandlerResult.Open(
                    "Sorry — should I go ahead? Yes or no.",
                    Retry(ctx.Pending, attempts));
        }
    }

    private static PendingAction Retry(PendingAction p, int attempts) => new()
    {
        Kind = p.Kind, Intent = p.Intent, LeaveId = p.LeaveId,
        Slots = p.Slots, IssuedAt = p.IssuedAt, Attempts = attempts
    };
}
