using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services;

public interface IIntentHandler
{
    /// Takes the whole context, not just the intent name: whether a handler
    /// can take a turn sometimes depends on more than the label. A bare
    /// "yes" with nothing pending is not a confirmation, it is chat.
    bool CanHandle(IntentContext ctx);
    Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default);
}
