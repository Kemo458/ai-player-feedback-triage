using System.Diagnostics;
using System.Text.Json;
using PlayerFeedback.Core.Common;
using PlayerFeedback.Core.Domain;
using Microsoft.Extensions.Options;

namespace PlayerFeedback.Core.Analysis;

public class OllamaFeedbackAnalyzer : IFeedbackAnalyzer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OllamaChatClient _client;
    private readonly LlmOptions _options;

    public OllamaFeedbackAnalyzer(OllamaChatClient client, IOptions<LlmOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public string Provider => "Ollama";
    public string Model => _options.Model;

    public async Task<FeedbackAnalysisResult> AnalyzeAsync(FeedbackAnalysisInput input, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var context = $"Game: {input.GameName}. Source: {input.Source}.";
        if (input.Rating is int r) context += $" Star rating: {r}/5.";
        if (!string.IsNullOrWhiteSpace(input.AppVersion)) context += $" App version: {input.AppVersion}.";
        var userPrompt = $"{context}\n<feedback>\n{input.Text}\n</feedback>";

        var raw = await _client.ChatAsync(
            AnalysisSchema.SystemPrompt,
            userPrompt,
            AnalysisSchema.Format(),
            _options.ReviewContextTokens,
            numPredict: 512,
            ct);

        sw.Stop();

        var parsed = Parse(raw);
        var (primary, tags, severity, toxicity, sentiment, summary, confidence, entities, conflicts) =
            AnalysisValidator.Validate(parsed, input.Text);

        return new FeedbackAnalysisResult(
            "1.0", primary, tags, severity, toxicity, sentiment, summary, confidence,
            entities, conflicts, Provider, Model, _options.PromptVersion,
            ContentHasher.Hash(raw), (int)sw.ElapsedMilliseconds);
    }

    private static RawAnalysis Parse(string raw)
    {
        var json = ExtractJson(raw);
        try
        {
            return JsonSerializer.Deserialize<RawAnalysis>(json, Json)
                ?? throw new AnalysisValidationException("Model returned null JSON.");
        }
        catch (JsonException ex)
        {
            throw new AnalysisValidationException($"Model returned invalid JSON: {ex.Message}");
        }
    }

    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new AnalysisValidationException("Model returned empty output.");
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) throw new AnalysisValidationException("No JSON object in model output.");
        return raw.Substring(start, end - start + 1);
    }
}
