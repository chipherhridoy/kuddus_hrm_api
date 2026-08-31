using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services;

public interface IIntentHandler
{
    bool CanHandle(string intent);
    Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default);
}
