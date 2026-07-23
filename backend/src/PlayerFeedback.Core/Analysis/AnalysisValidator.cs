using PlayerFeedback.Core.Common;
using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Core.Analysis;

/// <summary>Domain validation of model output (README §15.5).</summary>
public static class AnalysisValidator
{
    private static readonly string[] ValidTags = { "Bug", "Feature", "Lore", "Toxic" };

    public static (PrimaryCategory, List<string> tags, Severity, Toxicity, Sentiment, string summary,
        double confidence, List<AnalyzedEntity> entities, List<string> conflicts)
        Validate(RawAnalysis raw, string sourceText)
    {
        if (raw is null) throw new AnalysisValidationException("Null analysis.");
        if (raw.SchemaVersion != "1.0")
            throw new AnalysisValidationException($"Unexpected schema version '{raw.SchemaVersion}'.");

        var primary = ParseEnum<PrimaryCategory>(raw.PrimaryCategory, "primaryCategory");
        var severity = ParseEnum<Severity>(raw.Severity, "severity");
        var toxicity = ParseEnum<Toxicity>(raw.Toxicity, "toxicity");
        var sentiment = ParseEnum<Sentiment>(raw.Sentiment, "sentiment");

        var tags = (raw.Tags ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Where(t => ValidTags.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Select(t => ValidTags.First(v => v.Equals(t, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();

        var summary = (raw.Summary ?? string.Empty).Trim();
        if (summary.Length == 0) throw new AnalysisValidationException("Empty summary.");
        if (summary.Length > 240) summary = summary[..240];

        var confidence = raw.Confidence ?? 0.0;
        if (double.IsNaN(confidence)) confidence = 0.0;
        confidence = Math.Clamp(confidence, 0.0, 1.0);

        var conflicts = new List<string>();

        // Critical severity only valid for bugs.
        var hasBug = tags.Contains("Bug", StringComparer.OrdinalIgnoreCase);
        if (severity == Severity.Critical && !hasBug)
        {
            conflicts.Add("Critical severity without a Bug tag.");
            severity = Severity.Unknown;
        }

        // primaryCategory must be reflected by at least one tag, except Other.
        if (primary != PrimaryCategory.Other &&
            !tags.Contains(primary.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            conflicts.Add($"primaryCategory '{primary}' not present in tags.");
        }

        // Toxic dashboard view considers either toxicity=Toxic or the Toxic tag; keep them coherent.
        if (toxicity == Toxicity.Toxic && !tags.Contains("Toxic", StringComparer.OrdinalIgnoreCase))
            tags.Add("Toxic");

        var entities = new List<AnalyzedEntity>();
        foreach (var re in raw.Entities ?? new List<RawEntity>())
        {
            if (string.IsNullOrWhiteSpace(re.Name)) continue;
            if (!TryParseEntityType(re.Type, out var etype)) etype = EntityType.Other;
            var evidence = (re.Evidence ?? string.Empty).Trim();
            if (evidence.Length > 160) evidence = evidence[..160];

            // Evidence must actually appear in the source (normalized) — otherwise drop as a likely hallucination.
            var check = evidence.Length > 0 ? evidence : re.Name!;
            if (!TextNormalizer.Contains(sourceText, check)) continue;

            entities.Add(new AnalyzedEntity(
                etype,
                re.Name!.Trim(),
                string.IsNullOrWhiteSpace(re.NormalizedName)
                    ? TextNormalizer.Normalize(re.Name!)
                    : TextNormalizer.Normalize(re.NormalizedName!),
                evidence,
                Math.Clamp(re.Confidence ?? confidence, 0.0, 1.0)));
        }

        return (primary, tags, severity, toxicity, sentiment, summary, confidence, entities, conflicts);
    }

    private static TEnum ParseEnum<TEnum>(string? value, string field) where TEnum : struct
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(typeof(TEnum), parsed))
            return parsed;
        throw new AnalysisValidationException($"Invalid {field} value '{value}'.");
    }

    private static bool TryParseEntityType(string? value, out EntityType type) =>
        Enum.TryParse(value, ignoreCase: true, out type) && Enum.IsDefined(typeof(EntityType), type);
}
