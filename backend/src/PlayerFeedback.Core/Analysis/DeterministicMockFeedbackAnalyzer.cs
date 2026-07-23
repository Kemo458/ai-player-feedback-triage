using System.Diagnostics;
using System.Text.RegularExpressions;
using PlayerFeedback.Core.Common;
using PlayerFeedback.Core.Domain;
using Microsoft.Extensions.Options;

namespace PlayerFeedback.Core.Analysis;

/// <summary>
/// Deterministic, offline analyzer. Same input + seed => same output. Used for tests and a
/// guaranteed demo when no model is available. Never the accuracy benchmark for the real model.
/// </summary>
public partial class DeterministicMockFeedbackAnalyzer : IFeedbackAnalyzer
{
    private readonly LlmOptions _options;
    public DeterministicMockFeedbackAnalyzer(IOptions<LlmOptions> options) => _options = options.Value;

    public string Provider => "Mock";
    public string Model => "deterministic-mock-v1";

    private static readonly string[] BugWords =
        { "crash", "freeze", "frozen", "bug", "broken", "error", "stuck", "black screen", "won't load", "wont load", "disconnect", "lag", "glitch", "loading" };
    private static readonly string[] CriticalWords =
        { "crash", "freeze", "frozen", "can't login", "cant login", "cannot login", "data loss", "lost progress", "payment", "charged", "won't open", "wont open", "black screen" };
    private static readonly string[] FeatureWords =
        { "please add", "would be nice", "feature", "suggestion", "wish", "should add", "request", "add a", "add more", "please make" };
    private static readonly string[] LoreWords =
        { "story", "lore", "who is", "what happens", "ending", "backstory", "quest line", "questline", "plot" };
    private static readonly string[] ToxicWords =
        { "idiot", "trash", "garbage", "stupid", "hate this", "scam", "noob", "worst", "developers are", "kill yourself", "sucks" };
    private static readonly string[] PositiveWords =
        { "love", "great", "awesome", "amazing", "best", "fun", "excellent", "fantastic", "addictive", "enjoy" };

    [GeneratedRegex(@"\b\d+\.\d+(\.\d+)*\b")] private static partial Regex VersionRegex();
    [GeneratedRegex(@"\b(Pixel\s?\d*|iPhone(\s?\d+)?|Galaxy\s?\w+|Samsung\s?\w*|OnePlus\s?\w*|iPad)\b", RegexOptions.IgnoreCase)] private static partial Regex DeviceRegex();
    [GeneratedRegex(@"\b(Android|iOS|Windows|Linux)\b", RegexOptions.IgnoreCase)] private static partial Regex OsRegex();
    [GeneratedRegex(@"\b([A-Z][a-z]{2,})(\s[A-Z][a-z]{2,}){0,2}\b")] private static partial Regex ProperNounRegex();

    public async Task<FeedbackAnalysisResult> AnalyzeAsync(FeedbackAnalysisInput input, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var text = input.Text ?? string.Empty;
        var lower = text.ToLowerInvariant();

        // Deterministic simulated latency (50-250ms), seeded by content hash.
        var hash = ContentHasher.Hash(text);
        var delay = 50 + (int)(Convert.ToInt64(hash[..8], 16) % 200);
        // Injected scenarios for tests.
        if (lower.Contains("[[timeout]]")) { await Task.Delay(TimeSpan.FromSeconds(_options.TimeoutSeconds + 5), ct); }
        if (lower.Contains("[[fail]]")) throw new AnalysisValidationException("Injected malformed output.");
        await Task.Delay(delay, ct);

        var tags = new List<string>();
        bool isBug = BugWords.Any(lower.Contains);
        bool isFeature = FeatureWords.Any(lower.Contains);
        bool isLore = LoreWords.Any(lower.Contains);
        bool isToxic = ToxicWords.Any(lower.Contains);
        bool isPositive = PositiveWords.Any(lower.Contains);

        if (isBug) tags.Add("Bug");
        if (isFeature) tags.Add("Feature");
        if (isLore) tags.Add("Lore");
        if (isToxic) tags.Add("Toxic");

        var severity = Severity.Unknown;
        if (isBug)
            severity = CriticalWords.Any(lower.Contains) ? Severity.Critical
                : (lower.Contains("lag") || lower.Contains("disconnect")) ? Severity.High
                : Severity.Medium;

        var toxicity = isToxic ? Toxicity.Toxic : Toxicity.NonToxic;

        var sentiment =
            isToxic || isBug ? Sentiment.Negative :
            isPositive && isFeature ? Sentiment.Mixed :
            isPositive ? Sentiment.Positive :
            Sentiment.Neutral;
        if (input.Rating is int rate)
            sentiment = rate >= 4 && !isBug && !isToxic ? Sentiment.Positive
                : rate <= 2 ? Sentiment.Negative : sentiment;

        var primary =
            isBug ? PrimaryCategory.Bug :
            isToxic ? PrimaryCategory.Toxic :
            isFeature ? PrimaryCategory.Feature :
            isLore ? PrimaryCategory.Lore :
            PrimaryCategory.Other;

        var summary = BuildSummary(text, primary);
        double confidence = tags.Count == 0 ? 0.55 : (tags.Count == 1 ? 0.85 : 0.75);

        var entities = ExtractEntities(text);

        sw.Stop();
        return new FeedbackAnalysisResult(
            "1.0", primary, tags, severity, toxicity, sentiment, summary, confidence,
            entities, new List<string>(), Provider, Model, _options.PromptVersion,
            hash[..32], (int)sw.ElapsedMilliseconds);
    }

    private static string BuildSummary(string text, PrimaryCategory primary)
    {
        var clean = ContentHasher.Normalize(text).Replace('\n', ' ');
        var firstSentence = Regex.Split(clean, @"(?<=[.!?])\s+").FirstOrDefault(s => s.Trim().Length > 0)?.Trim() ?? clean;
        if (firstSentence.Length > 220) firstSentence = firstSentence[..220].TrimEnd() + "…";
        var prefix = primary switch
        {
            PrimaryCategory.Bug => "Bug report: ",
            PrimaryCategory.Feature => "Feature request: ",
            PrimaryCategory.Lore => "Lore question: ",
            PrimaryCategory.Toxic => "Toxic feedback: ",
            _ => "Feedback: "
        };
        var result = prefix + firstSentence;
        return result.Length > 240 ? result[..240] : result;
    }

    private static List<AnalyzedEntity> ExtractEntities(string text)
    {
        var found = new List<AnalyzedEntity>();
        var seen = new HashSet<string>();

        void Add(EntityType type, string name)
        {
            var key = type + "|" + TextNormalizer.Normalize(name);
            if (name.Length < 2 || !seen.Add(key)) return;
            found.Add(new AnalyzedEntity(type, name.Trim(), TextNormalizer.Normalize(name),
                Trim(name), 0.7));
        }

        foreach (Match m in VersionRegex().Matches(text)) Add(EntityType.AppVersion, m.Value);
        foreach (Match m in DeviceRegex().Matches(text)) Add(EntityType.Device, m.Value);
        foreach (Match m in OsRegex().Matches(text)) Add(EntityType.OperatingSystem, m.Value);
        foreach (Match m in ProperNounRegex().Matches(text))
        {
            if (found.Count >= 10) break;
            // Skip obvious sentence-start noise words.
            var v = m.Value.Trim();
            if (CommonWords.Contains(v.ToLowerInvariant())) continue;
            Add(EntityType.Other, v);
        }
        return found.Take(20).ToList();
    }

    private static string Trim(string s) => s.Length > 160 ? s[..160] : s;

    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    { "The", "This", "That", "Please", "When", "After", "Every", "Since", "Great", "Good", "Bad", "Also", "Now", "But", "And", "Why" };
}
