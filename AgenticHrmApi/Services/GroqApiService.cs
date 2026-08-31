using System.Net.Http.Headers;
using System.Text.Json;

namespace AgenticHrmApi.Services;

public class GroqApiService
{
    private readonly HttpClient _httpClient;
    private readonly string[] _apiKeys;
    private int _currentKeyIndex = 0;
    private readonly object _lock = new object();

    public GroqApiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var keys = configuration.GetSection("GroqApiKeys").Get<string[]>();
        if (keys == null || keys.Length == 0) throw new ArgumentNullException("GroqApiKeys are missing");
        _apiKeys = keys;
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

    public async Task<string> TranscribeAudioAsync(Stream audioStream, string fileName)
    {
        using var content = new MultipartFormDataContent();
        
        var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/m4a"); // Flutter uses m4a by default
        
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent("whisper-large-v3"), "model");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetNextApiKey());
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Groq API Error: {response.StatusCode} - {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        
        return result.GetProperty("text").GetString() ?? string.Empty;
    }
}
