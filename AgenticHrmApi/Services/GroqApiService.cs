using System.Net.Http.Headers;
using System.Text.Json;

namespace AgenticHrmApi.Services;

/// Who is speaking, so the recogniser can be biased toward their name and
/// the vocabulary of their department.
public record TranscriptionHints(string SpeakerName, string Department);

public record TranscriptionResult(string Text, double NoSpeechProb, double AvgLogProb)
{
    /// Whisper emits confident-sounding phrases on silence and noise —
    /// "Thank you.", "Subtitles by ..." — and they used to reach the
    /// reasoner as if the user had said them.
    public bool IsLikelyHallucination =>
        NoSpeechProb > WhisperTuning.MaxNoSpeechProb ||
        AvgLogProb < WhisperTuning.MinAvgLogProb;
}

public static class WhisperTuning
{
    public const string Model = "whisper-large-v3";

    /// Relaxed thresholds to avoid discarding soft, accented, or code-switched speech.
    public const double MaxNoSpeechProb = 0.8;
    public const double MinAvgLogProb = -1.6;

    /// Vocabulary bias. Whisper accepts up to 224 tokens of prompt and
    /// weights its decoding toward the words in it.
    /// Includes both English/Banglish and Bengali script keywords.
    public static string BuildPrompt(TranscriptionHints h) =>
        $"Kuddus, কদ্দুস, কুদ্দুস is the assistant name. Speaker: {h.SpeakerName}, Dept: {h.Department}. " +
        "Vocabulary: check in, check out, attendance, leave, apply, approve, reject, " +
        "chuti, ছুটি, kal, কাল, aaj, আজ, theke, থেকে, porjonto, পর্যন্ত, " +
        "osustho, অসুস্থ, biye, বিয়ে, ami eshechi, আমি এসেছি, office ashbo na, অফিসে আসবো না, shuno, শুনো.";
}

public class GroqApiService
{
    private readonly HttpClient _httpClient;
    private readonly string[] _apiKeys;
    private int _currentKeyIndex = 0;
    private readonly object _lock = new object();

    /// Pinned rather than auto-detected (spec D7): the reasoner's entire
    /// Banglish vocabulary and every worked example in its prompt are
    /// Latin-script. Auto-detected Bengali script bypasses all of it.
    public string Language { get; }

    public GroqApiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var keys = configuration.GetSection("GroqApiKeys").Get<string[]>();
        if (keys == null || keys.Length == 0) throw new ArgumentNullException("GroqApiKeys are missing");
        _apiKeys = keys;
        Language = configuration["WhisperLanguage"] ?? "en";
    }

    private string GetNextApiKey()
    {
        lock (_lock)
        {
            var key = _apiKeys[_currentKeyIndex];
            _currentKeyIndex = (_currentKeyIndex + 1) % _apiKeys.Length;
            return key;
        }
    }

    public async Task<TranscriptionResult> TranscribeAudioAsync(
        Stream audioStream, string fileName, TranscriptionHints hints,
        CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();

        var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/m4a"); // Flutter uses m4a by default

        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(WhisperTuning.Model), "model");
        content.Add(new StringContent(WhisperTuning.BuildPrompt(hints)), "prompt");
        content.Add(new StringContent("0"), "temperature");
        content.Add(new StringContent("verbose_json"), "response_format");
        if (!string.IsNullOrWhiteSpace(Language) && !Language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            content.Add(new StringContent(Language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetNextApiKey());
        request.Content = content;

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"Groq API Error: {response.StatusCode} - {error}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        var text = result.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;

        // Absent segments means we cannot judge confidence. Trust the
        // transcript rather than discarding real speech over a shape change.
        var noSpeech = 0.0;
        var avgLogProb = 0.0;
        if (result.TryGetProperty("segments", out var segs) &&
            segs.ValueKind == JsonValueKind.Array && segs.GetArrayLength() > 0)
        {
            var count = 0;
            foreach (var s in segs.EnumerateArray())
            {
                if (s.TryGetProperty("no_speech_prob", out var n)) noSpeech += n.GetDouble();
                if (s.TryGetProperty("avg_logprob", out var a)) avgLogProb += a.GetDouble();
                count++;
            }
            noSpeech /= count;
            avgLogProb /= count;
        }

        return new TranscriptionResult(text, noSpeech, avgLogProb);
    }
}
