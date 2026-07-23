using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PlayerFeedback.Core.Analysis;

/// <summary>Low-level Ollama /api/chat wrapper with JSON-schema structured output.</summary>
public class OllamaChatClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<OllamaChatClient> _logger;

    public OllamaChatClient(HttpClient http, IOptions<LlmOptions> options, ILogger<OllamaChatClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ChatAsync(
        string systemPrompt,
        string userPrompt,
        object? formatSchema,
        int numCtx,
        int numPredict,
        CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["think"] = false,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            ["options"] = new { temperature = 0, seed = 42, num_ctx = numCtx, num_predict = numPredict }
        };
        if (formatSchema is not null) body["format"] = formatSchema;

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/api/chat")
        {
            Content = JsonContent.Create(body)
        };

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var status = (int)resp.StatusCode;
            throw new OllamaException($"Ollama returned HTTP {status}.", status);
        }

        var dto = await resp.Content.ReadFromJsonAsync<ChatResponse>(Json, ct)
            ?? throw new OllamaException("Ollama returned an empty body.", 502);
        return dto.Message?.Content ?? string.Empty;
    }

    private class ChatResponse
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    private class ChatMessage
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}

public class OllamaException : Exception
{
    public int StatusCode { get; }
    public OllamaException(string message, int statusCode) : base(message) => StatusCode = statusCode;

    public bool IsTransient => StatusCode is 0 or 408 or 429 or 502 or 503 or 504;
}
