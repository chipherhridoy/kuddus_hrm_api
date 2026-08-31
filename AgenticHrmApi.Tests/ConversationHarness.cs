using AgenticHrmApi.Data;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Intents;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgenticHrmApi.Tests;

public static class ConversationHarness
{
    public static readonly DateTime Now = new(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc);

    public static ConversationService Make(string name, out AppDbContext db)
    {
        db = TestDb.Create(name);
        var clock = new FixedClock(Now);
        var leave = new LeaveIntentHandler(db, clock);
        var manager = new ManagerIntentHandler(db, clock);

        var router = new IntentRouter(
        [
            new AttendanceIntentHandler(new AttendanceService(db, clock)),
            leave, manager,
            new QueryIntentHandler(db, clock),
            new ControlIntentHandler(leave, manager),
            new ChatIntentHandler()
        ]);

        return new ConversationService(db, router, new LocalRuleReasoner(), clock,
            NullLogger<ConversationService>.Instance);
    }
}
