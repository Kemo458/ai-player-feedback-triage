namespace PlayerFeedback.Core.Domain;

public enum FeedbackSource { GooglePlay, Internal }

public enum FeedbackStatus { Pending, Processing, Completed, RetryScheduled, ManualReview, Failed }

public enum ImportStatus { Queued, Fetching, Persisting, Completed, PartiallyCompleted, Failed, Cancelled }

public enum PrimaryCategory { Bug, Feature, Lore, Toxic, Other }

public enum Severity { Critical, High, Medium, Low, Unknown }

public enum Toxicity { Toxic, NonToxic, Uncertain }

public enum Sentiment { Positive, Neutral, Negative, Mixed }

public enum EntityType
{
    Character, Weapon, Item, Location, Zone, Quest, GameMode,
    Ability, Device, OperatingSystem, AppVersion, Other
}

public enum SummaryStatus { Pending, Generating, Ready, Invalidated, Empty, Failed }
