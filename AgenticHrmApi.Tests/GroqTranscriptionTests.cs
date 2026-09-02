using System.Net;
using System.Text;
using AgenticHrmApi.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgenticHrmApi.Tests;

public class GroqTranscriptionTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private const string GoodBody = """
        {"text":"kal chuti lagbe","segments":[{"no_speech_prob":0.02,"avg_logprob":-0.21}]}
        """;

    private static GroqApiService Make(out StubHandler handler, string body = GoodBody, string? language = null)
    {
        handler = new StubHandler(HttpStatusCode.OK, body);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GroqApiKeys:0"] = "test-key",
                ["WhisperLanguage"] = language,
            })
            .Build();

        return new GroqApiService(new HttpClient(handler), config);
    }

    private static readonly TranscriptionHints Hints = new("Rahim Uddin", "Sales");

    private static Stream Audio() => new MemoryStream(Encoding.UTF8.GetBytes("fake audio"));

    [Fact]
    public async Task Returns_the_transcript()
    {
        var g = Make(out _);
        var r = await g.TranscribeAudioAsync(Audio(), "a.m4a", Hints);
        Assert.Equal("kal chuti lagbe", r.Text);
    }

    [Fact]
    public async Task Sends_the_vocabulary_prompt_language_temperature_and_format()
    {
        var g = Make(out var handler);
        await g.TranscribeAudioAsync(Audio(), "a.m4a", Hints);

        // The body is multipart/form-data, so assert on the part names as
        // well as the values — "en" alone appears inside "Content-Type".
        // Quotes are stripped because .NET writes `name=prompt` unquoted.
        var body = handler.LastBody!.Replace("\"", "");

        Assert.Contains("name=prompt", body);
        Assert.Contains("Kuddus", body);
        Assert.Contains("chuti", body);
        Assert.Contains("Rahim Uddin", body);

        Assert.Contains("name=response_format", body);
        Assert.Contains("verbose_json", body);

        Assert.Contains("name=temperature", body);
        Assert.Contains("name=language", body);
        Assert.Contains("name=model", body);
        Assert.Contains(WhisperTuning.Model, body);
    }

    [Fact]
    public void The_bias_prompt_names_Kuddus_and_the_speaker()
    {
        var p = WhisperTuning.BuildPrompt(Hints);

        Assert.Contains("Kuddus", p);
        Assert.Contains("Rahim Uddin", p);
        Assert.Contains("Sales", p);
    }

    [Fact]
    public void The_bias_prompt_carries_the_Banglish_the_reasoner_expects()
    {
        var p = WhisperTuning.BuildPrompt(Hints);

        foreach (var w in new[] { "chuti", "kal", "aaj", "osustho", "theke", "porjonto" })
            Assert.Contains(w, p);
    }

    [Fact]
    public void The_bias_prompt_stays_inside_Whispers_224_token_budget()
    {
        // Roughly four characters per token; stay well clear of the limit.
        Assert.True(WhisperTuning.BuildPrompt(Hints).Length < 800);
    }

    [Fact]
    public void Language_defaults_to_en_so_output_stays_latin_script()
    {
        // The reasoner's Banglish examples are all Latin-script (D7).
        Assert.Equal("en", Make(out _).Language);
    }

    [Fact]
    public void Language_is_configurable()
    {
        Assert.Equal("bn", Make(out _, language: "bn").Language);
    }

    [Fact]
    public async Task Auto_language_omits_language_form_field()
    {
        var g = Make(out var handler, language: "auto");
        await g.TranscribeAudioAsync(Audio(), "a.m4a", Hints);
        var body = handler.LastBody!.Replace("\"", "");
        Assert.DoesNotContain("name=language", body);
    }

    [Fact]
    public async Task A_silence_hallucination_is_reported_as_low_confidence()
    {
        const string hallucinated = """
            {"text":"Thank you.","segments":[{"no_speech_prob":0.94,"avg_logprob":-0.4}]}
            """;
        var g = Make(out _, body: hallucinated);

        var r = await g.TranscribeAudioAsync(Audio(), "a.m4a", Hints);
        Assert.True(r.IsLikelyHallucination);
    }

    [Fact]
    public async Task A_garbled_low_probability_transcript_is_low_confidence()
    {
        const string garbled = """
            {"text":"mmm hrrm","segments":[{"no_speech_prob":0.1,"avg_logprob":-1.8}]}
            """;
        var g = Make(out _, body: garbled);

        var r = await g.TranscribeAudioAsync(Audio(), "a.m4a", Hints);
        Assert.True(r.IsLikelyHallucination);
    }

    [Fact]
    public async Task A_clean_transcript_is_confident()
    {
        var r = await Make(out _).TranscribeAudioAsync(Audio(), "a.m4a", Hints);
        Assert.False(r.IsLikelyHallucination);
    }

    [Fact]
    public async Task A_response_without_segments_is_trusted()
    {
        // Never discard a real transcript because the shape surprised us.
        var g = Make(out _, body: """{"text":"check me in"}""");

        var r = await g.TranscribeAudioAsync(Audio(), "a.m4a", Hints);
        Assert.Equal("check me in", r.Text);
        Assert.False(r.IsLikelyHallucination);
    }
}
