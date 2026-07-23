namespace PlayerFeedback.Core.Domain;

public class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? GooglePlayPackageId { get; set; }
    public string? GooglePlayUrl { get; set; }
    public string? SubmissionTokenHash { get; set; }
    public bool SubmissionEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Feedback> Feedback { get; set; } = new();
}

public class Feedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    public FeedbackSource Source { get; set; }
    public string? ExternalId { get; set; }
    public string? AuthorName { get; set; }
    public string Text { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public int? Rating { get; set; }
    public string? AppVersion { get; set; }
    public string? Device { get; set; }
    public string? Locale { get; set; }

    public DateTime? SourceCreatedAt { get; set; }
    public DateTime? SourceUpdatedAt { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public FeedbackStatus Status { get; set; } = FeedbackStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ProcessingLeaseUntil { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public FeedbackAnalysis? Analysis { get; set; }
}

public class FeedbackAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FeedbackId { get; set; }
    public Feedback? Feedback { get; set; }

    public string SchemaVersion { get; set; } = "1.0";
    public PrimaryCategory PrimaryCategory { get; set; }
    public List<string> Tags { get; set; } = new();
    public Severity Severity { get; set; }
    public Toxicity Toxicity { get; set; }
    public Sentiment Sentiment { get; set; }
    public string Summary { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool RequiresManualReview { get; set; }

    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string? RawResponseHash { get; set; }
    public int DurationMilliseconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<FeedbackEntity> Entities { get; set; } = new();
}

public class FeedbackEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public FeedbackAnalysis? Analysis { get; set; }

    // Denormalized for cheap per-game aggregation.
    public Guid GameId { get; set; }
    public FeedbackSource Source { get; set; }
    public Sentiment Sentiment { get; set; }

    public EntityType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

public class GooglePlayImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    public string PackageId { get; set; } = string.Empty;
    public int RequestedCount { get; set; }
    public string Language { get; set; } = "en";
    public string Country { get; set; } = "us";
    public string Sort { get; set; } = "newest";
    public int? ScoreFilter { get; set; }

    public ImportStatus Status { get; set; } = ImportStatus.Queued;
    public int FetchedCount { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }

    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ProcessingLeaseUntil { get; set; }
    public bool CancelRequested { get; set; }

    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class AggregateSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }

    // Scope key components. Empty string = "all".
    public string SourceFilter { get; set; } = string.Empty;
    public string CategoryFilter { get; set; } = string.Empty;
    public string SeverityFilter { get; set; } = string.Empty;
    public string SentimentFilter { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;

    public string? InputFingerprint { get; set; }
    public int IncludedFeedbackCount { get; set; }
    public string Overview { get; set; } = string.Empty;
    public string ThemesJson { get; set; } = "[]";

    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;

    public SummaryStatus Status { get; set; } = SummaryStatus.Pending;
    public DateTime? ProcessingLeaseUntil { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public DateTime? InvalidatedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
