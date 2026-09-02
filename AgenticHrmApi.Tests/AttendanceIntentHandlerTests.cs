using AgenticHrmApi.Contracts;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Intents;
using Xunit;

namespace AgenticHrmApi.Tests;

public class AttendanceIntentHandlerTests
{
    private static (AttendanceIntentHandler h, User u) Make(string name, DateTime at)
    {
        var db = TestDb.Create(name);
        var svc = new AttendanceService(db, new FixedClock(at));
        return (new AttendanceIntentHandler(svc), db.Users.Find(3)!);
    }

    [Fact]
    public void Handles_only_attendance_intents()
    {
        var (h, u) = Make(nameof(Handles_only_attendance_intents), new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc));

        IntentContext Ctx(string intent) => new() { User = u, Intent = intent };

        Assert.True(h.CanHandle(Ctx("attendance.checkin")));
        Assert.True(h.CanHandle(Ctx("attendance.checkout")));
        Assert.False(h.CanHandle(Ctx("leave.apply")));
    }

    [Fact]
    public async Task Checkin_acts_immediately_with_no_confirmation()
    {
        var (h, u) = Make(nameof(Checkin_acts_immediately_with_no_confirmation), new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc));
        var r = await h.HandleAsync(new IntentContext { User = u, Intent = "attendance.checkin" });

        Assert.True(r.DidAct);
        Assert.Null(r.Pending);
        Assert.True(r.ConversationOpen);
        Assert.Contains("09:00", r.Reply);
    }

    [Fact]
    public async Task Checkout_branches_on_intent_not_notes()
    {
        var (h, u) = Make(nameof(Checkout_branches_on_intent_not_notes), new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc));
        await h.HandleAsync(new IntentContext { User = u, Intent = "attendance.checkin" });
        var r = await h.HandleAsync(new IntentContext { User = u, Intent = "attendance.checkout" });

        Assert.True(r.DidAct);
        Assert.Contains("Checked out", r.Reply);
    }

    [Fact]
    public async Task Duplicate_checkin_reports_without_acting()
    {
        var (h, u) = Make(nameof(Duplicate_checkin_reports_without_acting), new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc));
        await h.HandleAsync(new IntentContext { User = u, Intent = "attendance.checkin" });
        var r = await h.HandleAsync(new IntentContext { User = u, Intent = "attendance.checkin" });

        Assert.False(r.DidAct);
        Assert.Contains("Already checked in", r.Reply);
    }
}
