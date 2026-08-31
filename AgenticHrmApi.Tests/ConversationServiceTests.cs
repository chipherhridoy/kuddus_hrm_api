using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Intents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace AgenticHrmApi.Tests;

public class ConversationServiceTests
{
    private static string Json(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public async Task Text_only_request_needs_no_audio()
    {
        var svc = ConversationHarness.Make(nameof(Text_only_request_needs_no_audio), out _);
        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "check me in" });

        Assert.Equal("check me in", res.Transcript);
        Assert.True(res.DidAct);
    }

    [Fact]
    public async Task Four_turn_leave_conversation_writes_exactly_one_row()
    {
        var svc = ConversationHarness.Make(nameof(Four_turn_leave_conversation_writes_exactly_one_row), out var db);
        var history = new List<ConversationTurn>();

        var t1 = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "I need to take leave", History = Json(history) });
        Assert.True(t1.ConversationOpen);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
        history.Add(new ConversationTurn { Role = "user", Text = "I need to take leave" });
        history.Add(new ConversationTurn { Role = "kuddus", Text = t1.Reply });

        var t2 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "August 28 to August 30",
            History = Json(history), PendingAction = Json(t1.PendingAction!)
        });
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
        history.Add(new ConversationTurn { Role = "kuddus", Text = t2.Reply });

        var t3 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "family wedding",
            History = Json(history), PendingAction = Json(t2.PendingAction!)
        });
        Assert.Equal("applyLeave", t3.PendingAction!.Kind);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
        history.Add(new ConversationTurn { Role = "kuddus", Text = t3.Reply });

        var t4 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "yes",
            History = Json(history), PendingAction = Json(t3.PendingAction)
        });

        Assert.True(t4.DidAct);
        Assert.Equal(1, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Empty_transcript_keeps_the_conversation_open()
    {
        var svc = ConversationHarness.Make(nameof(Empty_transcript_keeps_the_conversation_open), out _);
        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "" });

        Assert.True(res.ConversationOpen);
        Assert.Contains("didn't catch", res.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BargeInPrefix_is_prepended_to_the_transcript()
    {
        var svc = ConversationHarness.Make(nameof(BargeInPrefix_is_prepended_to_the_transcript), out _);
        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "me in", BargeInPrefix = "check"
        });

        Assert.Equal("check me in", res.Transcript);
    }

    [Fact]
    public async Task Turn_cap_closes_a_runaway_conversation()
    {
        var svc = ConversationHarness.Make(nameof(Turn_cap_closes_a_runaway_conversation), out _);
        var history = Enumerable.Range(0, ConversationService.MaxTurns + 1)
            .Select(i => new ConversationTurn { Role = i % 2 == 0 ? "user" : "kuddus", Text = "hello" })
            .ToList();

        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "hello", History = Json(history) });
        Assert.False(res.ConversationOpen);
    }

    [Fact]
    public async Task Reply_is_capped_at_200_characters()
    {
        var svc = ConversationHarness.Make(nameof(Reply_is_capped_at_200_characters), out _);
        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "hello" });
        Assert.True(res.Reply.Length <= ConversationService.MaxReplyChars);
    }

    [Fact]
    public async Task Unknown_user_does_not_throw()
    {
        var svc = ConversationHarness.Make(nameof(Unknown_user_does_not_throw), out _);
        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 9999, Text = "check me in" });
        Assert.False(res.DidAct);
    }
}
