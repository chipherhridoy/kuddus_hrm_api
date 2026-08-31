using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services;

public class IntentRouter(IEnumerable<IIntentHandler> handlers)
{
    private readonly List<IIntentHandler> _handlers = handlers.ToList();

    public Task<HandlerResult> RouteAsync(IntentContext ctx, CancellationToken ct = default)
    {
        var handler = _handlers.FirstOrDefault(h => h.CanHandle(ctx.Intent));
        return handler is null
            ? Task.FromResult(HandlerResult.Open(
                "Sorry, I didn't follow. You can check in, apply for leave, or ask about your attendance."))
            : handler.HandleAsync(ctx, ct);
    }
}
