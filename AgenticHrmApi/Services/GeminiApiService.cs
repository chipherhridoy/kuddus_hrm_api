using System.Text;
using System.Text.Json;

namespace AgenticHrmApi.Services;

public class GeminiApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiApiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GeminiApiKey"] ?? string.Empty;
        _model = configuration["GeminiModel"] ?? "gemini-2.0-flash";
    }

    public async Task<string> ParseIntentAsync(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            // If no API key configured, use local rule-based intent parsing fallback
            return ParseIntentLocally(userMessage);
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
        
        var prompt = $@"
You are an HR Assistant. The user said: '{userMessage}'
Analyze the intent and return ONLY a valid JSON object matching one of these structures:
1. Leave Request: {{ ""intent"": ""leave"", ""startDate"": ""YYYY-MM-DD"", ""endDate"": ""YYYY-MM-DD"", ""reason"": ""..."" }}
2. Attendance: {{ ""intent"": ""attendance"", ""action"": ""checkin"" (or checkout) }}
3. Chat: {{ ""intent"": ""chat"", ""response"": ""...your friendly response in Banglish/English..."" }}

Do NOT wrap the JSON in markdown blocks (e.g. no ```json). Just return the raw JSON object.
";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            // If API key is invalid/expired or model retired, fallback gracefully to local rule parser
            return ParseIntentLocally(userMessage);
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        
        var responseText = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
            
        var cleaned = CleanJsonString(responseText ?? "{}");
        return cleaned;
    }

    public static string CleanJsonString(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(7);
        }
        else if (text.StartsWith("```"))
        {
            text = text.Substring(3);
        }

        if (text.EndsWith("```"))
        {
            text = text.Substring(0, text.Length - 3);
        }

        return text.Trim();
    }

    public static string ParseIntentLocally(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("check out") || lower.Contains("checkout") || lower.Contains("leaving"))
        {
            return "{\"intent\":\"attendance\",\"action\":\"checkout\"}";
        }
        if (lower.Contains("check in") || lower.Contains("checkin") || lower.Contains("present") || lower.Contains("attendance") || lower.Contains("arrived"))
        {
            return "{\"intent\":\"attendance\",\"action\":\"checkin\"}";
        }
        if (lower.Contains("leave") || lower.Contains("sick") || lower.Contains("vacation") || lower.Contains("chuti"))
        {
            var tomorrow = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
            var nextWeek = DateTime.UtcNow.Date.AddDays(3).ToString("yyyy-MM-dd");
            return $"{{\"intent\":\"leave\",\"startDate\":\"{tomorrow}\",\"endDate\":\"{nextWeek}\",\"reason\":\"{userMessage.Replace("\"", "'")}\"}}";
        }

        return "{\"intent\":\"chat\",\"response\":\"Hello! I am your Kuddus HRM voice assistant. You can ask me to check in, check out, or apply for leave.\"}";
    }
}
