using System.Net;
using AgenticHrmApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgenticHrmApi.Tests;

/// Guards the failure that let the entire LLM reasoning layer sit dead while
/// every request still returned 200: the configured model did not exist for
/// the credential, and ReasonAsync swallowed the 404 into its local fallback.
public class GeminiReasonerConfigTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        /// Statuses returned in order; anything past the end repeats [status].
        public Queue<HttpStatusCode> Sequence { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            var code = Sequence.Count > 0 ? Sequence.Dequeue() : status;
            return new HttpResponseMessage(code)
            {
                Content = new StringContent(GeminiBody)
            };
        }
    }

    /// A minimal well-formed generateContent response.
    private const string GeminiBody = """
        {"candidates":[{"content":{"parts":[{"text":"{\"intent\":\"leave.apply\",\"slots\":{\"startDate\":\"2026-09-01\"}}"}]}}]}
        """;

    private static GeminiReasoner Make(
        HttpStatusCode status,
        out StubHandler handler,
        string? model = null,
        string apiKey = "test-key")
    {
        handler = new StubHandler(status);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GeminiApiKey"] = apiKey,
                ["GeminiModel"] = model,
            })
            .Build();

        return new GeminiReasoner(
            new HttpClient(handler),
            config,
            new LocalRuleReasoner(),
            NullLogger<GeminiReasoner>.Instance);
    }

    [Fact]
    public void Default_model_is_not_the_unavailable_gemini_2_0_flash()
    {
        // gemini-2.0-flash 404s on this project. Shipping it as the default
        // is what caused the silent degradation.
        Assert.NotEqual("gemini-2.0-flash", GeminiReasoner.DefaultModel);
        Assert.NotEqual("gemini-1.5-flash", GeminiReasoner.DefaultModel);
    }

    [Fact]
    public void Default_model_is_pinned_not_a_floating_alias()
    {
        // A `-latest` alias can change how it follows the JSON instructions
        // in BuildPrompt with no change on our side.
        Assert.DoesNotContain("latest", GeminiReasoner.DefaultModel);
    }

    [Fact]
    public void Model_falls_back_to_the_default_when_config_is_absent()
    {
        var r = Make(HttpStatusCode.OK, out _);
        Assert.Equal(GeminiReasoner.DefaultModel, r.Model);
    }

    [Fact]
    public void Model_comes_from_configuration_when_set()
    {
        var r = Make(HttpStatusCode.OK, out _, model: "gemini-2.5-flash");
        Assert.Equal("gemini-2.5-flash", r.Model);
    }

    [Fact]
    public void HasApiKey_is_false_when_the_key_is_blank()
    {
        Assert.False(Make(HttpStatusCode.OK, out _, apiKey: "").HasApiKey);
        Assert.True(Make(HttpStatusCode.OK, out _).HasApiKey);
    }

    [Fact]
    public async Task VerifyModelAsync_is_false_when_the_model_does_not_exist()
    {
        var r = Make(HttpStatusCode.NotFound, out _, model: "gemini-2.0-flash");
        Assert.False(await r.VerifyModelAsync());
    }

    [Fact]
    public async Task VerifyModelAsync_is_true_when_the_model_exists()
    {
        var r = Make(HttpStatusCode.OK, out _);
        Assert.True(await r.VerifyModelAsync());
    }

    [Fact]
    public async Task VerifyModelAsync_probes_the_configured_model_by_name()
    {
        var r = Make(HttpStatusCode.OK, out var handler, model: "gemini-2.5-flash");
        await r.VerifyModelAsync();

        Assert.Contains("models/gemini-2.5-flash", handler.LastUrl);
    }

    [Fact]
    public async Task VerifyModelAsync_is_false_without_a_key_and_makes_no_call()
    {
        var r = Make(HttpStatusCode.OK, out var handler, apiKey: "");

        Assert.False(await r.VerifyModelAsync());
        Assert.Null(handler.LastUrl);
    }

    [Fact]
    public async Task Structured_extraction_runs_at_temperature_zero_and_demands_json()
    {
        // At the API default temperature of 1.0 the same sentence fills its
        // slots on one call and not the next.
        var db = TestDb.Create(nameof(Structured_extraction_runs_at_temperature_zero_and_demands_json));
        var r = Make(HttpStatusCode.OK, out var handler);

        await r.ReasonAsync(Input(db));

        Assert.Contains("\"temperature\":0", handler.LastBody!.Replace(" ", ""));
        Assert.Contains("application/json", handler.LastBody!);
    }

    [Fact]
    public async Task A_transient_429_is_retried_rather_than_downgrading_the_turn()
    {
        var db = TestDb.Create(nameof(A_transient_429_is_retried_rather_than_downgrading_the_turn));
        var r = Make(HttpStatusCode.OK, out var handler);
        handler.Sequence.Enqueue(HttpStatusCode.TooManyRequests);
        handler.Sequence.Enqueue(HttpStatusCode.OK);

        var result = await r.ReasonAsync(Input(db));

        Assert.Equal(2, handler.Calls);
        // Came from Gemini, not the rule parser: the parser fills no dates
        // on a fresh turn.
        Assert.True(result.Slots.ContainsKey("startDate"));
    }

    [Fact]
    public async Task A_503_is_also_treated_as_transient()
    {
        var db = TestDb.Create(nameof(A_503_is_also_treated_as_transient));
        var r = Make(HttpStatusCode.OK, out var handler);
        handler.Sequence.Enqueue(HttpStatusCode.ServiceUnavailable);
        handler.Sequence.Enqueue(HttpStatusCode.OK);

        await r.ReasonAsync(Input(db));

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Retries_are_bounded_then_it_falls_back()
    {
        var db = TestDb.Create(nameof(Retries_are_bounded_then_it_falls_back));
        var r = Make(HttpStatusCode.TooManyRequests, out var handler);

        var result = await r.ReasonAsync(Input(db, "check me in"));

        Assert.Equal(GeminiReasoner.MaxTransientRetries + 1, handler.Calls);
        Assert.Equal("attendance.checkin", result.Intent);   // local parser
    }

    [Fact]
    public async Task A_404_is_not_retried_because_it_is_a_config_error()
    {
        var db = TestDb.Create(nameof(A_404_is_not_retried_because_it_is_a_config_error));
        var r = Make(HttpStatusCode.NotFound, out var handler);

        await r.ReasonAsync(Input(db, "check me in"));

        Assert.Equal(1, handler.Calls);
    }

    private static ReasoningInput Input(AgenticHrmApi.Data.AppDbContext db, string utterance = "I want leave from Sept 1 to Sept 3") =>
        new()
        {
            User = db.Users.Find(3)!,
            Utterance = utterance,
            Today = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public async Task A_404_still_degrades_to_the_local_parser_rather_than_throwing()
    {
        // The graceful-degradation behaviour is correct and must stay —
        // the startup check is what makes it visible.
        var db = TestDb.Create(nameof(A_404_still_degrades_to_the_local_parser_rather_than_throwing));
        var r = Make(HttpStatusCode.NotFound, out _);

        var result = await r.ReasonAsync(new ReasoningInput
        {
            User = db.Users.Find(3)!,
            Utterance = "check me in",
            Today = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
        });

        Assert.Equal("attendance.checkin", result.Intent);
    }
}
