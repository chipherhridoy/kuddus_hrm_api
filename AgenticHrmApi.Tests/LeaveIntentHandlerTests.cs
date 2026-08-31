using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Intents;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgenticHrmApi.Tests;

public class LeaveIntentHandlerTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task No_dates_asks_for_dates_and_writes_nothing()
    {
        var db = TestDb.Create(nameof(No_dates_asks_for_dates_and_writes_nothing));
        var h = new LeaveIntentHandler(db, new FixedClock(Now));

        var r = await h.HandleAsync(new IntentContext { User = db.Users.Find(3)!, Intent = "leave.apply" });

        Assert.True(r.ConversationOpen);
        Assert.False(r.DidAct);
        Assert.Equal("collectingSlots", r.Pending!.Kind);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Dates_without_reason_asks_for_reason()
    {
        var db = TestDb.Create(nameof(Dates_without_reason_asks_for_reason));
        var h = new LeaveIntentHandler(db, new FixedClock(Now));

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!,
            Intent = "leave.apply",
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30" }
        });

        Assert.Contains("reason", r.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task All_slots_filled_asks_for_confirmation_and_still_writes_nothing()
    {
        var db = TestDb.Create(nameof(All_slots_filled_asks_for_confirmation_and_still_writes_nothing));
        var h = new LeaveIntentHandler(db, new FixedClock(Now));

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!,
            Intent = "leave.apply",
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "Family wedding" }
        });

        Assert.Equal("applyLeave", r.Pending!.Kind);
        Assert.False(r.DidAct);
        Assert.Contains("Family wedding", r.Reply);
        Assert.Contains("2026-08-28", r.Reply);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Commit_writes_exactly_one_pending_row()
    {
        var db = TestDb.Create(nameof(Commit_writes_exactly_one_pending_row));
        var h = new LeaveIntentHandler(db, new FixedClock(Now));

        var pending = new PendingAction
        {
            Kind = "applyLeave",
            Intent = "leave.apply",
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "Family wedding" },
            IssuedAt = Now
        };

        var r = await h.CommitAsync(new IntentContext { User = db.Users.Find(3)!, Intent = "leave.apply", Pending = pending });

        Assert.True(r.DidAct);
        var row = await db.LeaveRequests.SingleAsync();
        Assert.Equal("Pending", row.Status);
        Assert.Equal("Family wedding", row.Reason);
        Assert.Equal(DateTimeKind.Utc, row.StartDate.Kind);
    }

    [Fact]
    public async Task Ambiguous_date_asks_rather_than_guessing()
    {
        var db = TestDb.Create(nameof(Ambiguous_date_asks_rather_than_guessing));
        var h = new LeaveIntentHandler(db, new FixedClock(Now));

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!,
            Intent = "leave.apply",
            Slots = new() { ["startDate"] = "ambiguous:kal" }
        });

        Assert.Contains("tomorrow", r.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }
}
