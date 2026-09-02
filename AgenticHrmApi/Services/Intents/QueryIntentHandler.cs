using AgenticHrmApi.Contracts;
using AgenticHrmApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Services.Intents;

public class QueryIntentHandler(AppDbContext db, IClock clock) : IIntentHandler
{
    public bool CanHandle(IntentContext ctx) =>
        ctx.Intent is "query.attendance" or "query.leaves" or "query.stats";

    public async Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default)
    {
        var today = clock.UtcNow.Date;

        switch (ctx.Intent)
        {
            case "query.attendance":
            {
                var rec = await db.AttendanceRecords
                    .FirstOrDefaultAsync(a => a.UserId == ctx.User.Id && a.Date.Date == today, ct);

                if (rec is null) return HandlerResult.Open("You're not checked in yet today.");

                var late = rec.Status == "Late" ? " You were late." : "";
                return rec.CheckOutTime is null
                    ? HandlerResult.Open($"Yes, since {rec.CheckInTime:hh:mm tt}.{late}")
                    : HandlerResult.Open($"You checked out at {rec.CheckOutTime:hh:mm tt}.");
            }

            case "query.leaves":
            {
                var pending = await db.LeaveRequests
                    .CountAsync(l => l.UserId == ctx.User.Id && l.Status == "Pending", ct);

                return HandlerResult.Open(pending switch
                {
                    0 => "You have no pending leave requests.",
                    1 => "You have 1 pending leave request.",
                    _ => $"You have {pending} pending leave requests."
                });
            }

            default:
            {
                if (!ctx.User.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    return HandlerResult.Open("Only an admin can see team stats.");

                var present = await db.AttendanceRecords.CountAsync(a => a.Date.Date == today, ct);
                var late    = await db.AttendanceRecords.CountAsync(a => a.Date.Date == today && a.Status == "Late", ct);
                return HandlerResult.Open($"{present} present today, {late} late.");
            }
        }
    }
}
