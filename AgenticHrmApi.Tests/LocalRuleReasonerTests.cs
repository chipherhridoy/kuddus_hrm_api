using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using Xunit;

namespace AgenticHrmApi.Tests;

public class LocalRuleReasonerTests
{
    private static readonly DateTime Today = new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

    private static ReasoningInput In(string utterance, PendingAction? pending = null) => new()
    {
        User = TestDb.Create(Guid.NewGuid().ToString()).Users.Find(3)!,
        Utterance = utterance,
        History = [],
        Pending = pending,
        Today = Today
    };

    [Theory]
    [InlineData("check me in", "attendance.checkin")]
    [InlineData("I'm leaving", "attendance.checkout")]
    [InlineData("check out", "attendance.checkout")]
    [InlineData("I need leave", "leave.apply")]
    [InlineData("chuti lagbe", "leave.apply")]
    [InlineData("am I checked in", "query.attendance")]
    [InlineData("what can you do", "chat.help")]
    public async Task Maps_utterances_to_intents(string utterance, string expected)
    {
        var r = await new LocalRuleReasoner().ReasonAsync(In(utterance));
        Assert.Equal(expected, r.Intent);
    }

    [Fact]
    public async Task Control_words_become_control_intents_only_when_something_is_pending()
    {
        var reasoner = new LocalRuleReasoner();

        var withPending = await reasoner.ReasonAsync(In("yes", new PendingAction { Kind = "applyLeave" }));
        Assert.StartsWith("control.", withPending.Intent);

        var without = await reasoner.ReasonAsync(In("yes"));
        Assert.StartsWith("chat", without.Intent);
    }

    [Fact]
    public async Task Never_invents_dates_it_was_not_given()
    {
        var r = await new LocalRuleReasoner().ReasonAsync(In("I need leave"));
        Assert.False(r.Slots.ContainsKey("startDate"));
        Assert.False(r.Slots.ContainsKey("endDate"));
    }
}
