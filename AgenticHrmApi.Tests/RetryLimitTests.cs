using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace AgenticHrmApi.Tests;

public class RetryLimitTests
{
    private static string Json(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public async Task Unusable_slot_answers_abandon_after_two_reasks_writing_nothing()
    {
        var svc = ConversationHarness.Make(
            nameof(Unusable_slot_answers_abandon_after_two_reasks_writing_nothing), out var db);

        // Turn 1 opens slot collection.
        var t1 = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "I need leave" });
        Assert.Equal("collectingSlots", t1.PendingAction!.Kind);
        Assert.Equal(0, t1.PendingAction.Attempts);

        // Two unusable answers: attempts climbs, conversation stays open.
        var t2 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "the weather is nice",
            PendingAction = Json(t1.PendingAction)
        });
        Assert.True(t2.ConversationOpen);
        Assert.Equal(1, t2.PendingAction!.Attempts);

        var t3 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "bananas",
            PendingAction = Json(t2.PendingAction)
        });
        Assert.True(t3.ConversationOpen);
        Assert.Equal(2, t3.PendingAction!.Attempts);

        // The third exceeds the limit — abandon, nothing written.
        var t4 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "purple",
            PendingAction = Json(t3.PendingAction)
        });

        Assert.False(t4.ConversationOpen);
        Assert.Null(t4.PendingAction);
        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task A_usable_answer_resets_the_attempt_count()
    {
        var svc = ConversationHarness.Make(nameof(A_usable_answer_resets_the_attempt_count), out _);

        var t1 = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "I need leave" });
        var t2 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "gibberish", PendingAction = Json(t1.PendingAction!)
        });
        Assert.Equal(1, t2.PendingAction!.Attempts);

        var t3 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "August 28 to August 30", PendingAction = Json(t2.PendingAction)
        });

        Assert.Equal(0, t3.PendingAction!.Attempts);
    }

    [Fact]
    public async Task One_empty_transcript_keeps_the_conversation_open()
    {
        var svc = ConversationHarness.Make(nameof(One_empty_transcript_keeps_the_conversation_open), out _);

        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "" });

        Assert.True(res.ConversationOpen);
        Assert.Equal(ConversationService.EmptyTranscriptReply, res.Reply);
    }

    [Fact]
    public async Task Two_consecutive_empty_transcripts_close_the_conversation()
    {
        var svc = ConversationHarness.Make(nameof(Two_consecutive_empty_transcripts_close_the_conversation), out _);

        var history = new List<ConversationTurn>
        {
            new() { Role = "user", Text = "hello" },
            new() { Role = "kuddus", Text = ConversationService.EmptyTranscriptReply },
        };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "", History = Json(history)
        });

        Assert.False(res.ConversationOpen);
    }

    [Fact]
    public async Task An_understood_turn_between_empties_resets_the_streak()
    {
        var svc = ConversationHarness.Make(nameof(An_understood_turn_between_empties_resets_the_streak), out _);

        var history = new List<ConversationTurn>
        {
            new() { Role = "kuddus", Text = ConversationService.EmptyTranscriptReply },
            new() { Role = "user", Text = "check me in" },
            new() { Role = "kuddus", Text = "Checked in at 09:00." },
        };

        var res = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "", History = Json(history)
        });

        Assert.True(res.ConversationOpen);
    }
}
