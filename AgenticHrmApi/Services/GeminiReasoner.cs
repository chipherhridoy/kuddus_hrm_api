using System.Text;
using System.Text.Json;
using AgenticHrmApi.Contracts;

namespace AgenticHrmApi.Services;

public class GeminiReasoner(
    HttpClient http,
    IConfiguration config,
    LocalRuleReasoner fallback,
    ILogger<GeminiReasoner> log) : IReasoner
{
    private readonly string _apiKey = config["GeminiApiKey"] ?? string.Empty;
    private readonly string _model  = config["GeminiModel"]  ?? DefaultModel;

    /// Pinned to an explicit version rather than the `gemini-flash-latest`
    /// alias: an auto-updating model can change how it follows the JSON
    /// instructions in BuildPrompt without any change on our side.
    ///
    /// 2.5-flash over the newer 3.x flash models deliberately — measured on
    /// this credential, 3.7-flash returned 503 on 2 of 5 sequential calls
    /// (preview capacity) while 2.5-flash returned 200 on 5 of 5.
    public const string DefaultModel = "gemini-2.5-flash";

    public const int MaxTransientRetries = 2;
    public const int RetryBaseDelayMs = 250;

    private static bool IsTransient(System.Net.HttpStatusCode s) =>
        s is System.Net.HttpStatusCode.TooManyRequests
          or System.Net.HttpStatusCode.ServiceUnavailable
          or System.Net.HttpStatusCode.InternalServerError
          or System.Net.HttpStatusCode.BadGateway
          or System.Net.HttpStatusCode.GatewayTimeout;

    public string Model => _model;
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    /// Confirms the configured model actually exists for this credential.
    ///
    /// This exists because a wrong model name produces a 404 that
    /// ReasonAsync swallows into the local rule fallback — correct at
    /// runtime, but it means the entire LLM reasoning layer can be dead
    /// for the whole life of the process while every request still returns
    /// 200. That is exactly how `gemini-2.0-flash` went unnoticed.
    public async Task<bool> VerifyModelAsync(CancellationToken ct = default)
    {
        if (!HasApiKey) return false;

        try
        {
            var res = await http.GetAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/{_model}?key={_apiKey}", ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not reach Gemini to verify model {Model}.", _model);
            return false;
        }
    }

    public static string BuildPrompt(ReasoningInput input)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Kuddus, an HR voice assistant. Reply in the user's language (English, Bangla, or Banglish).");
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

        if (input.Pending is not null)
            sb.AppendLine($"Awaiting a yes/no about: {input.Pending.Kind}. Prefer a control.* intent.");

        sb.AppendLine("Return ONLY raw JSON: { \"intent\": \"...\", \"slots\": { }, \"reply\": \"...\" }");
        sb.AppendLine("Valid intents: attendance.checkin, attendance.checkout, leave.apply,");
        sb.AppendLine("leave.approve, leave.reject, query.attendance, query.leaves, query.stats,");
        sb.AppendLine("chat.smalltalk, chat.help, control.confirm, control.deny, control.cancel.");
        sb.AppendLine("Slot keys: startDate, endDate (YYYY-MM-DD), reason, who.");
        sb.AppendLine("NEVER invent a date you were not told. Omit the slot instead.");
        sb.AppendLine("Bare \"kal\" in Bangla means BOTH tomorrow and yesterday — it is ambiguous.");
        sb.AppendLine("For any ambiguous date emit \"ambiguous:kal\" as the slot value, never a guess.");
        sb.AppendLine("\"reply\" is for questions and small talk only. Never state a time, date, name, or outcome.");
        sb.AppendLine("Keep \"reply\" under 200 characters. No markdown fences.");
        sb.AppendLine();

        // Banglish vocabulary and worked examples. Without these the model
        // extracts dates reliably from English but misses them in
        // code-switched Bangla, which is the D2 decision half-working.
        sb.AppendLine("Banglish vocabulary: chuti = leave; chuti lagbe / chuti nibo = I need leave;");
        sb.AppendLine("theke = from/since; porjonto = until; kal = tomorrow OR yesterday (ambiguous);");
        sb.AppendLine("aaj = today; ashbo na = I will not come; biye = wedding; osustho = sick;");
        sb.AppendLine("office ashbo na = I will not come to the office (this means leave).");
        sb.AppendLine();
        sb.AppendLine("Examples (extract slots in Bangla and Banglish exactly as you would in English):");
        sb.AppendLine("  'kal chuti lagbe'");
        sb.AppendLine("    -> {\"intent\":\"leave.apply\",\"slots\":{\"startDate\":\"ambiguous:kal\"}}");
        sb.AppendLine("  'chuti lagbe Sept 1 theke Sept 3, biye ache'");
        sb.AppendLine($"    -> {{\"intent\":\"leave.apply\",\"slots\":{{\"startDate\":\"{input.Today.Year}-09-01\",\"endDate\":\"{input.Today.Year}-09-03\",\"reason\":\"biye\"}}}}");
        sb.AppendLine("  'ami osustho, aaj chuti nibo'");
        sb.AppendLine($"    -> {{\"intent\":\"leave.apply\",\"slots\":{{\"startDate\":\"{input.Today:yyyy-MM-dd}\",\"endDate\":\"{input.Today:yyyy-MM-dd}\",\"reason\":\"osustho\"}}}}");
        sb.AppendLine("  'ami eshechi'");
        sb.AppendLine("    -> {\"intent\":\"attendance.checkin\",\"slots\":{}}");
        sb.AppendLine();
        sb.AppendLine($"User said: '{input.Utterance}'");
        return sb.ToString();
    }

    public async Task<ReasoningResult> ReasonAsync(ReasoningInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return await fallback.ReasonAsync(input, ct);

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            // Without generationConfig this runs at the API default temperature
            // of 1.0, which for structured extraction means the same sentence
            // fills its slots on one call and not the next. Intent parsing is
            // not a creative task — pin it to 0 and demand JSON.
            var body = new
            {
                contents = new[] { new { parts = new[] { new { text = BuildPrompt(input) } } } },
                generationConfig = new
                {
                    temperature = 0.0,
                    responseMimeType = "application/json"
                }
            };
            var payload = JsonSerializer.Serialize(body);
            HttpResponseMessage? res = null;

            // 429 and 503 are transient quota/capacity responses, not config
            // errors. Without a retry a single one silently downgrades the turn
            // to the rule parser, which cannot fill slots from one sentence.
            // Delays stay short — this sits in a voice turn's critical path.
            for (var attempt = 0; attempt <= MaxTransientRetries; attempt++)
            {
                res?.Dispose();
                res = await http.PostAsync(url,
                    new StringContent(payload, Encoding.UTF8, "application/json"), ct);

                if (res.IsSuccessStatusCode || !IsTransient(res.StatusCode)) break;

                if (attempt < MaxTransientRetries)
                {
                    var delayMs = RetryBaseDelayMs * (attempt + 1);
                    log.LogInformation(
                        "Gemini returned {Status}; retrying in {Delay}ms (attempt {Next}/{Max}).",
                        res.StatusCode, delayMs, attempt + 2, MaxTransientRetries + 1);
                    await Task.Delay(delayMs, ct);
                }
            }

            if (!res!.IsSuccessStatusCode)
            {
                log.LogWarning("Gemini returned {Status}; using local fallback.", res.StatusCode);
                return await fallback.ReasonAsync(input, ct);
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var text = doc.RootElement.GetProperty("candidates")[0]
                .GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString() ?? "{}";

            return Parse(GeminiApiService.CleanJsonString(text)) ?? await fallback.ReasonAsync(input, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Gemini reasoning failed; using local fallback.");
            return await fallback.ReasonAsync(input, ct);
        }
    }

    private static ReasoningResult? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var slots = new Dictionary<string, string>();
            if (root.TryGetProperty("slots", out var s) && s.ValueKind == JsonValueKind.Object)
                foreach (var p in s.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String)
                        slots[p.Name] = p.Value.GetString()!;

            return new ReasoningResult
            {
                Intent = root.TryGetProperty("intent", out var i) ? i.GetString() ?? "chat" : "chat",
                Slots = slots,
                Reply = root.TryGetProperty("reply", out var r) ? r.GetString() : null
            };
        }
        catch { return null; }   // prose instead of JSON — caller falls back
    }
}
