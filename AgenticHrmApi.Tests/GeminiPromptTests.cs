using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using Xunit;

namespace AgenticHrmApi.Tests;

public class GeminiPromptTests
{
    private static ReasoningInput In() => new()
    {
        User = TestDb.Create(Guid.NewGuid().ToString()).Users.Find(3)!,
        Utterance = "kal chuti lagbe",
        History = [new ConversationTurn { Role = "kuddus", Text = "Which dates?" }],
        Today = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void Prompt_carries_todays_date_so_relative_words_can_resolve()
    {
        var p = GeminiReasoner.BuildPrompt(In());
        Assert.Contains("2026-08-24", p);
        Assert.Contains("Monday", p);
    }

    [Fact]
    public void Prompt_carries_user_identity_and_role()
    {
        var p = GeminiReasoner.BuildPrompt(In());
        Assert.Contains("Rahim Uddin", p);
        Assert.Contains("Employee", p);
    }

    [Fact]
    public void Prompt_carries_history()
    {
        Assert.Contains("Which dates?", GeminiReasoner.BuildPrompt(In()));
    }

    [Fact]
    public void Prompt_instructs_ambiguity_over_guessing_for_kal()
    {
        var p = GeminiReasoner.BuildPrompt(In());
        Assert.Contains("ambiguous:", p);
        Assert.Contains("kal", p, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_lists_every_intent()
    {
        var p = GeminiReasoner.BuildPrompt(In());
        foreach (var i in new[]
        {
            "attendance.checkin", "attendance.checkout", "leave.apply",
            "leave.approve", "leave.reject", "query.attendance",
            "query.leaves", "query.stats", "chat.smalltalk", "chat.help"
        })
            Assert.Contains(i, p);
    }

    [Fact]
    public void Prompt_teaches_the_Banglish_vocabulary_it_needs()
    {
        // Without these the model extracts dates reliably in English but
        // misses them in code-switched Bangla — the D2 decision half-working.
        var p = GeminiReasoner.BuildPrompt(In());
        foreach (var word in new[] { "chuti", "theke", "porjonto", "aaj", "ashbo na", "biye", "osustho" })
            Assert.Contains(word, p);
    }

    [Fact]
    public void Prompt_carries_worked_Banglish_examples()
    {
        var p = GeminiReasoner.BuildPrompt(In());
        Assert.Contains("kal chuti lagbe", p);
        Assert.Contains("ambiguous:kal", p);
        Assert.Contains("ami eshechi", p);
    }

    [Fact]
    public void Banglish_examples_use_the_real_current_date_not_a_stale_literal()
    {
        // The 'aaj' example must resolve to today, or it teaches a wrong date.
        var p = GeminiReasoner.BuildPrompt(In());
        Assert.Contains("2026-08-24", p);
    }
}
