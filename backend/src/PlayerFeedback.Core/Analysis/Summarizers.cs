using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PlayerFeedback.Core.Analysis;

public class OllamaAggregateSummarizer : IAggregateSummarizer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OllamaChatClient _client;
    private readonly LlmOptions _options;

    public OllamaAggregateSummarizer(OllamaChatClient client, IOptions<LlmOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public string Provider => "Ollama";
    public string Model => _options.Model;

    public async Task<SummaryResult> SummarizeAsync(SummaryInput input, CancellationToken ct)
    {
        var joined = string.Join("\n", input.Summaries.Take(100).Select((s, i) => $"{i + 1}. {s}"));
        var system = """
You summarize a set of already-classified player feedback items for a game team.
Use ONLY the provided per-item summaries as evidence. Do not invent facts or numbers.
Produce a short neutral overview (3-5 sentences) and a list of up to 6 recurring themes
(short noun phrases). Output JSON only, following the schema.
""";
        var user = $"Game: {input.GameName}. Scope: {input.ScopeDescription}. Total items: {input.TotalCount}.\n\nItems:\n{joined}";

        var format = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                // Ollama converts JSON-schema string limits into grammar repetitions.
                // Its default unbounded-string expansion is 2,000 characters, which
                // exceeds llama.cpp's grammar complexity limit for this schema.
                overview = new { type = "string", maxLength = 600 },
                themes = new
                {
                    type = "array",
                    maxItems = 6,
                    items = new { type = "string", maxLength = 80 }
                }
            },
            required = new[] { "overview", "themes" }
        };

        var raw = await _client.ChatAsync(system, user, format, _options.SummaryContextTokens, 700, ct);
        var parsed = ParseOverview(raw);

        var themes = (parsed.Themes ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => new SummaryTheme(t.Trim(), CountMentions(t, input.Summaries)))
            .ToList();

        return new SummaryResult(parsed.Overview ?? string.Empty, themes, Provider, Model);
    }

    private static int CountMentions(string theme, IReadOnlyList<string> summaries)
    {
        var words = theme.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3).ToArray();
        if (words.Length == 0) return 0;
        return summaries.Count(s => words.Any(w => s.ToLowerInvariant().Contains(w)));
    }

    private static OverviewDto ParseOverview(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return new OverviewDto { Overview = raw.Trim(), Themes = new() };
        try
        {
            return JsonSerializer.Deserialize<OverviewDto>(raw.Substring(start, end - start + 1), Json)
                ?? new OverviewDto { Overview = "", Themes = new() };
        }
        catch (JsonException)
        {
            return new OverviewDto { Overview = raw.Trim(), Themes = new() };
        }
    }

    private class OverviewDto
    {
        [JsonPropertyName("overview")] public string? Overview { get; set; }
        [JsonPropertyName("themes")] public List<string>? Themes { get; set; }
    }
}

/// <summary>Offline summarizer: template overview + keyword-frequency themes.</summary>
public class MockAggregateSummarizer : IAggregateSummarizer
{
    public string Provider => "Mock";
    public string Model => "deterministic-mock-v1";

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","for","with","that","this","from","have","report","feedback","request","question",
        "game","player","when","after","about","would","could","should","bug","toxic","lore","feature","user","into","your"
    };

    public Task<SummaryResult> SummarizeAsync(SummaryInput input, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in input.Summaries)
            foreach (var w in s.Split(new[] { ' ', ',', '.', '!', '?', ':', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var word = w.Trim().ToLowerInvariant();
                if (word.Length < 4 || Stop.Contains(word)) continue;
                counts[word] = counts.GetValueOrDefault(word) + 1;
            }

        var themes = counts.OrderByDescending(kv => kv.Value).Take(6)
            .Select(kv => new SummaryTheme(kv.Key, kv.Value)).ToList();

        var top = string.Join(", ", themes.Take(3).Select(t => t.Name));
        var overview = input.TotalCount == 0
            ? "No feedback matched this scope yet."
            : $"{input.TotalCount} item(s) in scope ({input.ScopeDescription}). " +
              (top.Length > 0 ? $"Recurring topics include {top}. " : "") +
              "This is a deterministic offline summary; enable the LLM provider for narrative synthesis.";

        return Task.FromResult(new SummaryResult(overview, themes, Provider, Model));
    }
}
