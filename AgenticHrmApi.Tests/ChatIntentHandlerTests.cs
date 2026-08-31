using AgenticHrmApi.Contracts;
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
            ProposedReply = proposed
        };
    }

    [Fact]
    public void Handles_chat_intents_only()
    {
        var h = new ChatIntentHandler();
        Assert.True(h.CanHandle("chat.smalltalk"));
        Assert.True(h.CanHandle("chat.help"));
        Assert.False(h.CanHandle("leave.apply"));
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
