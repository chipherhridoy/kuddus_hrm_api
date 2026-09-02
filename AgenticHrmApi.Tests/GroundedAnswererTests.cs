using System.Net;
using AgenticHrmApi.Contracts;
using AgenticHrmApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgenticHrmApi.Tests;

public class GroundedAnswererTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }
        public Queue<HttpStatusCode> Sequence { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            var code = Sequence.Count > 0 ? Sequence.Dequeue() : status;
            return new HttpResponseMessage(code) { Content = new StringContent(body) };
        }
    }

    private const string GoodBody = """
        {"candidates":[{"content":{"parts":[{"text":"Dhaka is about 31 degrees and humid right now. Want the details?"}]}}]}
        """;

    private static GroundedAnswerer Make(
        out StubHandler handler,
        HttpStatusCode status = HttpStatusCode.OK,
        string body = GoodBody,
        string apiKey = "test-key",
        string? enabled = null,
        string? model = null)
    {
        handler = new StubHandler(status, body);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GeminiApiKey"] = apiKey,
                ["GroundedAnswersEnabled"] = enabled,
                ["AnswerModel"] = model,
            })
            .Build();

        return new GroundedAnswerer(
            new HttpClient(handler), config, NullLogger<GroundedAnswerer>.Instance);
    }

    private static AnswerInput In(string utterance = "what is the weather in Dhaka")
    {
        var db = TestDb.Create(Guid.NewGuid().ToString());
        return new AnswerInput
        {
            User = db.Users.Find(3)!,
            Utterance = utterance,
            History = [new ConversationTurn { Role = "kuddus", Text = "Yes?" }],
            Today = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    [Fact]
    public async Task Returns_the_models_prose()
    {
        var a = Make(out _);
        var text = await a.AnswerAsync(In());
        Assert.Contains("Dhaka", text);
    }

    [Fact]
    public async Task Asks_for_search_grounding_and_not_json_mode()
    {
        // D2, verified against the live API: googleSearch together with
        // responseMimeType:application/json returns HTTP 400. Asking for
        // JSON here would 400 every single answer.
        var a = Make(out var handler);
        await a.AnswerAsync(In());

        Assert.Contains("googleSearch", handler.LastBody!);
        Assert.DoesNotContain("responseMimeType", handler.LastBody!);
    }

    [Fact]
    public async Task Uses_the_configured_answer_model()
    {
        var a = Make(out var handler, model: "gemini-2.5-flash");
        await a.AnswerAsync(In());
        Assert.Contains("models/gemini-2.5-flash", handler.LastUrl!);
    }

    [Fact]
    public async Task Returns_null_without_an_api_key_and_makes_no_call()
    {
        var a = Make(out var handler, apiKey: "");
        Assert.Null(await a.AnswerAsync(In()));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task The_kill_switch_stops_it_making_any_call()
    {
        var a = Make(out var handler, enabled: "false");
        Assert.False(a.Enabled);
        Assert.Null(await a.AnswerAsync(In()));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Enabled_by_default()
    {
        Assert.True(Make(out _).Enabled);
    }

    [Fact]
    public async Task A_transient_503_is_retried()
    {
        var a = Make(out var handler);
        handler.Sequence.Enqueue(HttpStatusCode.ServiceUnavailable);
        handler.Sequence.Enqueue(HttpStatusCode.OK);

        Assert.NotNull(await a.AnswerAsync(In()));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Retries_are_bounded_then_it_gives_up_with_null()
    {
        var a = Make(out var handler, status: HttpStatusCode.TooManyRequests);

        Assert.Null(await a.AnswerAsync(In()));
        Assert.Equal(GroundedAnswerer.MaxTransientRetries + 1, handler.Calls);
    }

    [Fact]
    public async Task A_400_is_not_retried_because_it_is_a_config_error()
    {
        var a = Make(out var handler, status: HttpStatusCode.BadRequest);

        Assert.Null(await a.AnswerAsync(In()));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_malformed_response_returns_null_rather_than_throwing()
    {
        var a = Make(out _, body: "not json at all");
        Assert.Null(await a.AnswerAsync(In()));
    }

    [Fact]
    public async Task An_empty_candidate_returns_null()
    {
        var a = Make(out _, body: """{"candidates":[]}""");
        Assert.Null(await a.AnswerAsync(In()));
    }

    [Fact]
    public void Prompt_carries_identity_date_history_and_the_utterance()
    {
        var p = GroundedAnswerer.BuildPrompt(In());

        Assert.Contains("Rahim Uddin", p);
        Assert.Contains("2026-08-31", p);
        Assert.Contains("Yes?", p);
        Assert.Contains("what is the weather in Dhaka", p);
    }

    [Fact]
    public void Prompt_forbids_the_model_stating_HR_facts()
    {
        // Code states outcomes; the model states prose. Attendance times and
        // leave balances come from the database, never from the model.
        var p = GroundedAnswerer.BuildPrompt(In());

        Assert.Contains("attendance", p, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never", p, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prompt_asks_for_three_sentences_and_an_offer_to_expand()
    {
        var p = GroundedAnswerer.BuildPrompt(In());

        Assert.Contains("three sentences", p, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Want the details?", p);
    }
}
