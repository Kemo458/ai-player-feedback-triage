namespace PlayerFeedback.Api.Workers;

public class WorkerOptions
{
    public int AnalysisConcurrency { get; set; } = 1;
    public int ImportConcurrency { get; set; } = 1;
    public int LeaseSeconds { get; set; } = 120;
    public int PollIntervalMilliseconds { get; set; } = 1000;
}

public class FeedbackOptions
{
    public int MaxTextLength { get; set; } = 5000;
    public double ManualReviewConfidenceThreshold { get; set; } = 0.65;
}

public static class RetrySchedule
{
    // Suggested delays: 5s, 30s, 2m (README §12.4).
    private static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)
    };

    public const int MaxAttempts = 3;

    public static DateTime NextAttempt(int attemptCount)
    {
        var idx = Math.Clamp(attemptCount - 1, 0, Delays.Length - 1);
        var jitterMs = (attemptCount * 137) % 1000; // deterministic small jitter
        return DateTime.UtcNow.Add(Delays[idx]).AddMilliseconds(jitterMs);
    }
}
