using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services.Intents;

public class AttendanceIntentHandler(AttendanceService attendance) : IIntentHandler
{
    public bool CanHandle(IntentContext ctx) =>
        ctx.Intent is "attendance.checkin" or "attendance.checkout";

    public async Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default)
    {
        var outcome = ctx.Intent == "attendance.checkout"
            ? await attendance.CheckOutAsync(ctx.User.Id, "via Kuddus", ct)
            : await attendance.CheckInAsync(ctx.User.Id, null, null, "via Kuddus", ct);

        // Reply text comes from the service, which composes it from DB values (spec 9.4).
        return outcome.Success
            ? HandlerResult.Acted(outcome.Message)
            : HandlerResult.Open(outcome.Message);
    }
}
