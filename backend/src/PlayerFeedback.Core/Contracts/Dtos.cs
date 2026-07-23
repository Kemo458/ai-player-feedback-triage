using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Core.Contracts;

public record Paged<T>(IReadOnlyList<T> Items, string? NextCursor);

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTime ExpiresAt);

public record CreateGameRequest(string Name, string? GooglePlayUrl);
public record GameDto(
    Guid Id, string Name, string? GooglePlayUrl, string? GooglePlayPackageId,
    string? IconUrl, bool SubmissionEnabled, string? SubmissionToken, DateTime CreatedAt);

public record ImportRequest(string Url, int Count = 100, string Language = "en",
    string Country = "us", string Sort = "newest", int? Score = null);

public record ImportJobDto(
    Guid Id, Guid GameId, string Status, int RequestedCount, int FetchedCount,
    int InsertedCount, int UpdatedCount, int SkippedCount, int FailedCount,
    string? LastErrorCode, string? LastErrorMessage,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt);

public record PublicFeedbackRequest(string Text, int? Rating, string? AppVersion, string? Device, string? Locale);
public record AcceptedDto(Guid Id, string Status);

public record EntityDto(string Type, string Name, string NormalizedName, string Evidence, double Confidence);
public record AnalysisDto(
    string PrimaryCategory, IReadOnlyList<string> Tags, string Severity, string Toxicity,
    string Sentiment, string Summary, double Confidence, bool RequiresManualReview,
    string Provider, string Model, DateTime CreatedAt, IReadOnlyList<EntityDto> Entities);

public record FeedbackDto(
    Guid Id, Guid GameId, string Source, string? ExternalId, string? Author, string Text, int? Rating,
    string? AppVersion, string? Device, DateTime? SourceCreatedAt, DateTime ImportedAt,
    string Status, int AttemptCount, string? LastErrorCode, string? LastErrorMessage,
    AnalysisDto? Analysis);

public record DashboardDto(
    Totals Totals, ProcessingCounts Processing,
    Dictionary<string, int> Categories, Dictionary<string, int> Severities,
    Dictionary<string, int> Sentiments, int CriticalBugs, int Toxic,
    int FailuresAndManualReview, IReadOnlyList<TopEntity> TopEntities,
    Dictionary<string, int> RatingDistribution);
public record Totals(int Total, Dictionary<string, int> BySource);
public record ProcessingCounts(int Pending, int Processing, int Completed, int RetryScheduled, int ManualReview, int Failed);
public record TopEntity(string NormalizedName, string Type, int Count);

public record SummaryThemeDto(string Name, int Count);
public record SummaryDto(
    Guid Id, string Status, string Overview, IReadOnlyList<SummaryThemeDto> Themes,
    int IncludedFeedbackCount, DateTime? GeneratedAt, DateTime? InvalidatedAt,
    string Provider, string Model);

public record EntityAggregateDto(
    string NormalizedName, string Type, int MentionCount, int FeedbackCount,
    Dictionary<string, int> SourceBreakdown, Dictionary<string, int> SentimentBreakdown,
    IReadOnlyList<string> Evidence);

public static class Mappers
{
    public static GameDto ToDto(this Game g, string? submissionToken = null, string? iconUrl = null) =>
        new(g.Id, g.Name, g.GooglePlayUrl, g.GooglePlayPackageId, iconUrl,
            g.SubmissionEnabled, submissionToken, g.CreatedAt);

    public static ImportJobDto ToDto(this GooglePlayImportJob j) =>
        new(j.Id, j.GameId, j.Status.ToString(), j.RequestedCount, j.FetchedCount, j.InsertedCount,
            j.UpdatedCount, j.SkippedCount, j.FailedCount, j.LastErrorCode, j.LastErrorMessage,
            j.CreatedAt, j.StartedAt, j.CompletedAt);

    public static FeedbackDto ToDto(this Feedback f) =>
        new(f.Id, f.GameId, f.Source.ToString(), f.ExternalId, f.AuthorName, f.Text, f.Rating, f.AppVersion, f.Device,
            f.SourceCreatedAt, f.ImportedAt, f.Status.ToString(), f.AttemptCount, f.LastErrorCode,
            f.LastErrorMessage, f.Analysis?.ToDto());

    public static AnalysisDto ToDto(this FeedbackAnalysis a) =>
        new(a.PrimaryCategory.ToString(), a.Tags, a.Severity.ToString(), a.Toxicity.ToString(),
            a.Sentiment.ToString(), a.Summary, a.Confidence, a.RequiresManualReview, a.Provider, a.Model,
            a.CreatedAt, a.Entities.Select(e => new EntityDto(e.Type.ToString(), e.Name, e.NormalizedName, e.Evidence, e.Confidence)).ToList());
}
