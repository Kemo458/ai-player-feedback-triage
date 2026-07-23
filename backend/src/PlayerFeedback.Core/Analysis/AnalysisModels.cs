using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Core.Analysis;

public record FeedbackAnalysisInput(
    Guid FeedbackId,
    string GameName,
    FeedbackSource Source,
    string Text,
    int? Rating,
    string? AppVersion,
    string? Locale);

public record AnalyzedEntity(
    EntityType Type,
    string Name,
    string NormalizedName,
    string Evidence,
    double Confidence);

public record FeedbackAnalysisResult(
    string SchemaVersion,
    PrimaryCategory PrimaryCategory,
    IReadOnlyList<string> Tags,
    Severity Severity,
    Toxicity Toxicity,
    Sentiment Sentiment,
    string Summary,
    double Confidence,
    IReadOnlyList<AnalyzedEntity> Entities,
    IReadOnlyList<string> Conflicts,
    string Provider,
    string Model,
    string PromptVersion,
    string? RawResponseHash,
    int DurationMilliseconds);

public interface IFeedbackAnalyzer
{
    string Provider { get; }
    string Model { get; }
    Task<FeedbackAnalysisResult> AnalyzeAsync(FeedbackAnalysisInput input, CancellationToken cancellationToken);
}

public record SummaryTheme(string Name, int Count);

public record SummaryInput(
    string GameName,
    string ScopeDescription,
    int TotalCount,
    IReadOnlyList<string> Summaries);

public record SummaryResult(string Overview, IReadOnlyList<SummaryTheme> Themes, string Provider, string Model);

public interface IAggregateSummarizer
{
    string Provider { get; }
    string Model { get; }
    Task<SummaryResult> SummarizeAsync(SummaryInput input, CancellationToken cancellationToken);
}

/// <summary>Thrown when model output cannot be validated into a domain-safe result.</summary>
public class AnalysisValidationException : Exception
{
    public AnalysisValidationException(string message) : base(message) { }
}
