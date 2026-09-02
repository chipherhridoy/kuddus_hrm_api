using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Intents;
using Xunit;

namespace AgenticHrmApi.Tests;

public class ChatIntentHandlerTests
{
    private static IntentContext Ctx(string intent, string? proposed = null)
    {
        var db = TestDb.Create(Guid.NewGuid().ToString());
        return new IntentContext
        {
            User = db.Users.Find(3)!,
            Intent = intent,
            Transcript = "test utterance",
            ProposedReply = proposed
        };
    }

    private sealed class FakeAnswerer(string? answer) : IAnswerer
    {
        public AnswerInput? LastInput { get; private set; }
        public int Calls { get; private set; }

        public Task<string?> AnswerAsync(AnswerInput input, CancellationToken ct = default)
        {
            Calls++;
            LastInput = input;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public void Handles_the_open_question_intent()
    {
        var h = new ChatIntentHandler(new FakeAnswerer("x"));
        Assert.True(h.CanHandle(Ctx("chat.answer")));
        Assert.True(h.CanHandle(Ctx("chat.smalltalk")));
        Assert.False(h.CanHandle(Ctx("attendance.checkin")));
    }

    [Fact]
    public async Task An_open_question_is_answered_by_the_answerer()
    {
        var fake = new FakeAnswerer("Dhaka is about 31 degrees right now.");
        var r = await new ChatIntentHandler(fake).HandleAsync(Ctx("chat.answer"));

        Assert.Equal("Dhaka is about 31 degrees right now.", r.Reply);
        Assert.Equal(1, fake.Calls);
        Assert.True(r.ConversationOpen);
        Assert.False(r.DidAct);
        Assert.Null(r.Pending);
    }

    [Fact]
    public async Task An_answered_question_gets_the_raised_reply_cap()
    {
        var r = await new ChatIntentHandler(new FakeAnswerer("ok")).HandleAsync(Ctx("chat.answer"));
        Assert.Equal(ChatIntentHandler.MaxAnswerChars, r.MaxReplyChars);
    }

    [Fact]
    public async Task A_dead_answerer_falls_back_to_Geminis_wording()
    {
        var r = await new ChatIntentHandler(new FakeAnswerer(null))
            .HandleAsync(Ctx("chat.answer", "I'm not sure about that."));

        Assert.Equal("I'm not sure about that.", r.Reply);
    }

    [Fact]
    public async Task A_dead_answerer_with_no_wording_falls_back_to_help()
    {
        var r = await new ChatIntentHandler(new FakeAnswerer(null)).HandleAsync(Ctx("chat.answer"));
        Assert.Equal(ChatIntentHandler.HelpReply, r.Reply);
    }

    [Fact]
    public async Task No_answerer_configured_behaves_exactly_as_before()
    {
        var r = await new ChatIntentHandler().HandleAsync(Ctx("chat.smalltalk", "Good morning!"));
        Assert.Equal("Good morning!", r.Reply);
        Assert.Equal(HandlerResult.DefaultMaxReplyChars, r.MaxReplyChars);
    }

    [Fact]
    public async Task Help_never_calls_the_answerer()
    {
        // "what can you do" is about this app, not the world.
        var fake = new FakeAnswerer("some web answer");
        var r = await new ChatIntentHandler(fake).HandleAsync(Ctx("chat.help"));

        Assert.Equal(ChatIntentHandler.HelpReply, r.Reply);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task The_answerer_receives_the_transcript_user_and_history()
    {
        var fake = new FakeAnswerer("ok");
        var db = TestDb.Create(Guid.NewGuid().ToString());
        var ctx = new IntentContext
        {
            User = db.Users.Find(3)!,
            Intent = "chat.answer",
            Transcript = "who won the world cup",
            History = [new ConversationTurn { Role = "kuddus", Text = "Yes?" }]
        };

        await new ChatIntentHandler(fake).HandleAsync(ctx);

        Assert.Equal("who won the world cup", fake.LastInput!.Utterance);
        Assert.Equal("Rahim Uddin", fake.LastInput!.User.Name);
        Assert.Single(fake.LastInput!.History);
    }

    [Fact]
    public void Handles_chat_intents_only()
    {
        var h = new ChatIntentHandler();
        Assert.True(h.CanHandle(Ctx("chat.smalltalk")));
        Assert.True(h.CanHandle(Ctx("chat.help")));
        Assert.False(h.CanHandle(Ctx("leave.apply")));
    }

    [Fact]
    public async Task Help_lists_what_Kuddus_can_do()
    {
        var r = await new ChatIntentHandler().HandleAsync(Ctx("chat.help"));
        Assert.Contains("check in", r.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.True(r.ConversationOpen);
        Assert.False(r.DidAct);
    }

    [Fact]
    public async Task Small_talk_uses_Geminis_wording_when_provided()
    {
        var r = await new ChatIntentHandler().HandleAsync(
            Ctx("chat.smalltalk", "Good morning! How can I help?"));
        Assert.Equal("Good morning! How can I help?", r.Reply);
    }

    [Fact]
    public async Task Small_talk_falls_back_to_help_when_Gemini_gave_nothing()
    {
        var r = await new ChatIntentHandler().HandleAsync(Ctx("chat.smalltalk"));
        Assert.Equal(ChatIntentHandler.HelpReply, r.Reply);
    }

    [Fact]
    public async Task Never_acts_and_never_leaves_a_pending_action()
    {
        var r = await new ChatIntentHandler().HandleAsync(Ctx("chat.smalltalk", "Hello!"));
        Assert.False(r.DidAct);
        Assert.Null(r.Pending);
    }
}
