using System.Text;
using System.Text.Json;

namespace AgenticHrmApi.Services;

/// Answers anything that is not an HR action, grounded in Google Search.
///
/// Deliberately mirrors GeminiReasoner's retry and degradation discipline
/// but shares no code with it: the two calls want opposite configurations.
/// The reasoner pins temperature 0 and demands JSON because extraction is
/// not a creative task; this one is generative prose and must NOT request
/// JSON, because googleSearch and responseMimeType are mutually exclusive.
public class GroundedAnswerer(
    HttpClient http,
    IConfiguration config,
    ILogger<GroundedAnswerer> log) : IAnswerer
{
    private readonly string _apiKey = config["GeminiApiKey"] ?? string.Empty;
    private readonly string _model = config["AnswerModel"] ?? DefaultModel;
    private readonly bool _enabled = config["GroundedAnswersEnabled"] is not "false";

    public const string DefaultModel = "gemini-2.5-flash";
    public const int MaxTransientRetries = 2;
    public const int RetryBaseDelayMs = 250;

    public string Model => _model;

    /// Kill switch: turns open-domain answering off without a deploy.
    /// Kuddus falls back to the canned help reply, which is how it behaved
    /// before this feature existed.
    public bool Enabled => _enabled && !string.IsNullOrWhiteSpace(_apiKey);

    private static bool IsTransient(System.Net.HttpStatusCode s) =>
        s is System.Net.HttpStatusCode.TooManyRequests
          or System.Net.HttpStatusCode.ServiceUnavailable
          or System.Net.HttpStatusCode.InternalServerError
          or System.Net.HttpStatusCode.BadGateway
          or System.Net.HttpStatusCode.GatewayTimeout;

    public static string BuildPrompt(AnswerInput input)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Kuddus, a voice assistant. You are being listened to, not read,");
        sb.AppendLine("so answer the way a person would speak.");
        sb.AppendLine("Reply in the user's language (English, Bangla, or Banglish).");
        sb.AppendLine($"Today is {input.Today:yyyy-MM-dd} ({input.Today:dddd}), timezone UTC.");
        sb.AppendLine($"Speaking to: {input.User.Name}, role {input.User.Role}, department {input.User.Department}.");
        sb.AppendLine();

        if (input.History.Count > 0)
        {
            sb.AppendLine("Conversation so far:");
            foreach (var t in input.History)
                sb.AppendLine($"  {t.Role}: {t.Text}{(t.Truncated ? " [cut off]" : "")}");
            sb.AppendLine();
        }

        sb.AppendLine("Rules:");
        sb.AppendLine("1. Answer in at most three sentences.");
        sb.AppendLine("2. If there is genuinely more worth saying, end with a short offer");
        sb.AppendLine("   such as 'Want the details?'. Otherwise do not offer.");
        sb.AppendLine("3. NEVER state this company's HR facts — attendance times, check-in or");
        sb.AppendLine("   check-out times, leave balances, leave approvals, or anyone's record.");
        sb.AppendLine("   Those come from the HR system, never from you. If asked, say you will");
        sb.AppendLine("   look it up and let the user ask again plainly.");
        sb.AppendLine("4. If you could not verify live information, say you could not check it.");
        sb.AppendLine("   Never invent a current price, weather, score, or news event.");
        sb.AppendLine("5. Plain speech only. No markdown, no bullet lists, no headings, no URLs.");
        sb.AppendLine();
        sb.AppendLine($"The user said: '{input.Utterance}'");
        return sb.ToString();
    }

    public async Task<string?> AnswerAsync(AnswerInput input, CancellationToken ct = default)
    {
        if (!Enabled) return null;

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            // No responseMimeType here, deliberately: requesting JSON
            // alongside the googleSearch tool is rejected by the API with
            // "Tool use with a response mime type: 'application/json' is
            // unsupported" (spec D2).
            var body = new
            {
                contents = new[] { new { parts = new[] { new { text = BuildPrompt(input) } } } },
                tools = new[] { new { googleSearch = new { } } }
            };
            var payload = JsonSerializer.Serialize(body);
            HttpResponseMessage? res = null;

            for (var attempt = 0; attempt <= MaxTransientRetries; attempt++)
            {
                res?.Dispose();
                res = await http.PostAsync(url,
                    new StringContent(payload, Encoding.UTF8, "application/json"), ct);

                if (res.IsSuccessStatusCode || !IsTransient(res.StatusCode)) break;

                if (attempt < MaxTransientRetries)
                    await Task.Delay(RetryBaseDelayMs * (attempt + 1), ct);
            }

            if (!res!.IsSuccessStatusCode)
            {
                log.LogWarning("Grounded answer returned {Status}; falling back.", res.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
                return null;

            var text = candidates[0]
                .GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString();

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception ex)
        {
            // Never throw into a voice turn — the caller speaks the fallback.
            log.LogError(ex, "Grounded answering failed; falling back.");
            return null;
        }
    }
}
