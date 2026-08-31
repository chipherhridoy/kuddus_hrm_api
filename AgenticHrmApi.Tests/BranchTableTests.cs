using AgenticHrmApi.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace AgenticHrmApi.Tests;

public class BranchTableTests
{
    private static string Json(object o) => JsonSerializer.Serialize(o);

    // Table A: Awaiting confirmation
    [Fact]
    public async Task TableA_Affirmative_commits_the_action()
    {
        var svc = ConversationHarness.Make(nameof(TableA_Affirmative_commits_the_action), out var db);
        var pending = new PendingAction
        {
            Kind = "applyLeave", Intent = "leave.apply", IssuedAt = ConversationHarness.Now,
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "wedding" }
        };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "yes", PendingAction = Json(pending)
        });

        Assert.True(res.DidAct);
        Assert.True(res.ConversationOpen);
        Assert.Equal(1, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task TableA_Negative_cancels_and_keeps_open()
    {
        var svc = ConversationHarness.Make(nameof(TableA_Negative_cancels_and_keeps_open), out var db);
        var pending = new PendingAction
        {
            Kind = "applyLeave", Intent = "leave.apply", IssuedAt = ConversationHarness.Now,
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "wedding" }
        };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "no", PendingAction = Json(pending)
        });

        Assert.False(res.DidAct);
        Assert.True(res.ConversationOpen);
        Assert.Null(res.PendingAction);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task TableA_Cancelling_cancels_and_closes()
    {
        var svc = ConversationHarness.Make(nameof(TableA_Cancelling_cancels_and_closes), out var db);
        var pending = new PendingAction
        {
            Kind = "applyLeave", Intent = "leave.apply", IssuedAt = ConversationHarness.Now,
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "wedding" }
        };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "never mind", PendingAction = Json(pending)
        });

        Assert.False(res.DidAct);
        Assert.False(res.ConversationOpen);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task TableA_Correction_reopens_collection()
    {
        var svc = ConversationHarness.Make(nameof(TableA_Correction_reopens_collection), out var db);
        var pending = new PendingAction
        {
            Kind = "applyLeave", Intent = "leave.apply", IssuedAt = ConversationHarness.Now,
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "wedding" }
        };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "no, make it sick leave", PendingAction = Json(pending)
        });

        Assert.False(res.DidAct);
        Assert.True(res.ConversationOpen);
        Assert.NotNull(res.PendingAction);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task TableA_Intent_switch_abandons_confirmation_and_runs_new_intent()
    {
        var svc = ConversationHarness.Make(nameof(TableA_Intent_switch_abandons_confirmation_and_runs_new_intent), out var db);
        var pending = new PendingAction
        {
            Kind = "applyLeave", Intent = "leave.apply", IssuedAt = ConversationHarness.Now,
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "wedding" }
        };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "check me in", PendingAction = Json(pending)
        });

        Assert.True(res.DidAct);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
        Assert.Equal(1, await db.AttendanceRecords.CountAsync());
    }

    [Fact]
    public async Task TableA_Unparseable_once_reasks_and_unparseable_twice_closes()
    {
        var svc = ConversationHarness.Make(nameof(TableA_Unparseable_once_reasks_and_unparseable_twice_closes), out var db);
        var pending = new PendingAction
        {
            Kind = "applyLeave", Intent = "leave.apply", IssuedAt = ConversationHarness.Now,
            Slots = new() { ["startDate"] = "2026-08-28", ["endDate"] = "2026-08-30", ["reason"] = "wedding" },
            Attempts = 0
        };

        var first = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "random noise", PendingAction = Json(pending)
        });
        Assert.True(first.ConversationOpen);
        Assert.Equal(1, first.PendingAction!.Attempts);

        var second = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "more random noise", PendingAction = Json(first.PendingAction)
        });
        Assert.False(second.ConversationOpen);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    // Table B: Slot collection
    [Fact]
    public async Task TableB_Ambiguous_value_prompts_for_clarification()
    {
        var svc = ConversationHarness.Make(nameof(TableB_Ambiguous_value_prompts_for_clarification), out _);
        var pending = new PendingAction { Kind = "collectingSlots", Intent = "leave.apply", Slots = new() };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "kal chuti lagbe", PendingAction = Json(pending)
        });

        Assert.True(res.ConversationOpen);
        Assert.Contains("tomorrow", res.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("yesterday", res.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TableB_Cancelling_closes_dialogue()
    {
        var svc = ConversationHarness.Make(nameof(TableB_Cancelling_closes_dialogue), out var db);
        var pending = new PendingAction { Kind = "collectingSlots", Intent = "leave.apply", Slots = new() };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "never mind", PendingAction = Json(pending)
        });

        Assert.False(res.ConversationOpen);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    // Table C: Post-action follow up
    [Fact]
    public async Task TableC_Negative_answers_anything_else_with_closing()
    {
        var svc = ConversationHarness.Make(nameof(TableC_Negative_answers_anything_else_with_closing), out _);
        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "no" });

        Assert.False(res.DidAct);
    }

    // Table D: Interrupted flows
    [Fact]
    public async Task TableD_Switch_mid_flow_writes_only_the_new_action()
    {
        var svc = ConversationHarness.Make(nameof(TableD_Switch_mid_flow_writes_only_the_new_action), out var db);
        var pending = new PendingAction { Kind = "collectingSlots", Intent = "leave.apply", Slots = new() };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "check me in", PendingAction = Json(pending)
        });

        Assert.True(res.DidAct);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
        Assert.Equal(1, await db.AttendanceRecords.CountAsync());
    }

    // Table E: Unprompted control word
    [Fact]
    public async Task TableE_Control_word_with_nothing_pending_does_not_act()
    {
        var svc = ConversationHarness.Make(nameof(TableE_Control_word_with_nothing_pending_does_not_act), out var db);
        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "yes" });

        Assert.False(res.DidAct);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
        Assert.Equal(0, await db.AttendanceRecords.CountAsync());
    }
}
