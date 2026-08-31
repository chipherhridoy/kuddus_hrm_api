using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Intents;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgenticHrmApi.Tests;

public class ControlIntentHandlerTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

    private static ControlIntentHandler Make(string name, out AgenticHrmApi.Data.AppDbContext db)
    {
        db = TestDb.Create(name);
        var clock = new FixedClock(Now);
        return new ControlIntentHandler(
            new LeaveIntentHandler(db, clock),
            new ManagerIntentHandler(db, clock));
    }

    private static PendingAction ApplyLeave() => new()
    {
        Kind = "applyLeave", Intent = "leave.apply", IssuedAt = Now,
        Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "Family wedding" }
    };

    [Fact]
    public async Task Affirmative_commits_the_pending_action()
    {
        var h = Make(nameof(Affirmative_commits_the_pending_action), out var db);

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!, Intent = "control.confirm",
            Transcript = "yes", Pending = ApplyLeave()
        });

        Assert.True(r.DidAct);
        Assert.Equal(1, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Negative_writes_nothing_and_keeps_the_conversation_open()
    {
        var h = Make(nameof(Negative_writes_nothing_and_keeps_the_conversation_open), out var db);

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!, Intent = "control.deny",
            Transcript = "no", Pending = ApplyLeave()
        });

        Assert.False(r.DidAct);
        Assert.True(r.ConversationOpen);
        Assert.Null(r.Pending);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Cancelling_writes_nothing_and_closes()
    {
        var h = Make(nameof(Cancelling_writes_nothing_and_closes), out var db);

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!, Intent = "control.cancel",
            Transcript = "never mind", Pending = ApplyLeave()
        });

        Assert.False(r.ConversationOpen);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Truncated_readback_refuses_to_confirm()
    {
        var h = Make(nameof(Truncated_readback_refuses_to_confirm), out var db);

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!, Intent = "control.confirm",
            Transcript = "yes", Pending = ApplyLeave(),
            History = [new ConversationTurn { Role = "kuddus", Text = "Leave from...", Truncated = true }]
        });

        Assert.False(r.DidAct);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
        Assert.NotNull(r.Pending);          // re-asked, not discarded
    }

    [Fact]
    public async Task Control_word_with_nothing_pending_does_not_act()
    {
        var h = Make(nameof(Control_word_with_nothing_pending_does_not_act), out var db);

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!, Intent = "control.confirm",
            Transcript = "yes", Pending = null
        });

        Assert.False(r.DidAct);
        Assert.Contains("what would you like", r.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unparseable_answer_reasks_once_then_abandons()
    {
        var h = Make(nameof(Unparseable_answer_reasks_once_then_abandons), out var db);
        var pending = ApplyLeave();

        var first = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!, Intent = "control.confirm",
            Transcript = "the weather is nice", Pending = pending
        });
        Assert.True(first.ConversationOpen);
        Assert.Equal(1, first.Pending!.Attempts);

        var second = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!, Intent = "control.confirm",
            Transcript = "bananas", Pending = first.Pending
        });

        Assert.False(second.ConversationOpen);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }
}
