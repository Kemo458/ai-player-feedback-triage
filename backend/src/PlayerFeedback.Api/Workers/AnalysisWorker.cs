using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlayerFeedback.Api.Activity;
using PlayerFeedback.Api.Hubs;
using PlayerFeedback.Core.Analysis;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Api.Workers;

public class AnalysisWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly WorkerOptions _worker;
    private readonly FeedbackOptions _feedback;
    private readonly IActivityLog _activity;
    private readonly ILogger<AnalysisWorker> _logger;

    public AnalysisWorker(IServiceProvider services, IOptions<WorkerOptions> worker,
        IOptions<FeedbackOptions> feedback, IActivityLog activity, ILogger<AnalysisWorker> logger)
    {
        _services = services;
        _worker = worker.Value;
        _feedback = feedback.Value;
        _activity = activity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lanes = Math.Max(1, _worker.AnalysisConcurrency);
        var tasks = Enumerable.Range(0, lanes).Select(i => Lane(i, stoppingToken)).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task Lane(int lane, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool worked = false;
            try
            {
                worked = await ProcessOne(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Analysis lane {Lane} loop error", lane);
            }
            if (!worked)
                await Task.Delay(_worker.PollIntervalMilliseconds, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private async Task<bool> ProcessOne(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var claimed = await ClaimAsync(db, ct);
        if (claimed is null) return false;

        var feedback = await db.Feedback.Include(f => f.Game)
            .FirstOrDefaultAsync(f => f.Id == claimed.Value, ct);
        if (feedback is null) return true;

        var analyzer = scope.ServiceProvider.GetRequiredService<IFeedbackAnalyzer>();
        var notifier = scope.ServiceProvider.GetRequiredService<IFeedbackNotifier>();

        var gameName = feedback.Game?.Name ?? "Unknown";
        var input = new FeedbackAnalysisInput(feedback.Id, gameName,
            feedback.Source, feedback.Text, feedback.Rating, feedback.AppVersion, feedback.Locale);

        _activity.Emit("run", $"analyzing {ShortId.Of(feedback.Id)} · {gameName} · {feedback.Source} → {analyzer.Provider} {analyzer.Model}", feedback.GameId);

        try
        {
            var result = await analyzer.AnalyzeAsync(input, ct);
            await StoreSuccess(db, feedback, result, ct);
            await notifier.FeedbackCompleted(feedback.GameId, feedback.Id);
            var mr = result.Confidence < _feedback.ManualReviewConfidenceThreshold || result.Conflicts.Count > 0;
            _activity.Emit(mr ? "warn" : "ok",
                $"{ShortId.Of(feedback.Id)} → {result.PrimaryCategory}/{result.Severity} · {result.Sentiment}"
                + $" · conf {result.Confidence:0.00} · {result.DurationMilliseconds}ms{(mr ? " · needs review" : "")}",
                feedback.GameId);
            _logger.LogInformation("Analyzed feedback {Id} -> {Cat}/{Sev} conf={Conf}",
                feedback.Id, result.PrimaryCategory, result.Severity, result.Confidence);
        }
        catch (AnalysisValidationException ex)
        {
            // Invalid/malformed output: retry once, then manual review.
            if (feedback.AttemptCount <= 1)
                Reschedule(feedback, "invalid_output", ex.Message);
            else
                ToManualReview(feedback, "invalid_output", ex.Message);
            await db.SaveChangesAsync(ct);
            _activity.Emit("warn", $"{ShortId.Of(feedback.Id)} invalid model output — {(feedback.Status == FeedbackStatus.ManualReview ? "sent to manual review" : "retrying")}", feedback.GameId);
        }
        catch (OllamaException ex) when (ex.IsTransient)
        {
            HandleTransient(feedback, "llm_unavailable", ex.Message);
            await db.SaveChangesAsync(ct);
            _activity.Emit("warn", $"{ShortId.Of(feedback.Id)} LLM unavailable (HTTP {ex.StatusCode}) — {(feedback.Status == FeedbackStatus.Failed ? "failed" : "retry scheduled")}", feedback.GameId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            HandleTransient(feedback, "llm_timeout", "Analysis timed out.");
            await db.SaveChangesAsync(ct);
            _activity.Emit("warn", $"{ShortId.Of(feedback.Id)} timed out — {(feedback.Status == FeedbackStatus.Failed ? "failed" : "retry scheduled")}", feedback.GameId);
        }
        catch (HttpRequestException ex)
        {
            HandleTransient(feedback, "llm_unavailable", ex.Message);
            await db.SaveChangesAsync(ct);
            _activity.Emit("warn", $"{ShortId.Of(feedback.Id)} LLM connection error — {(feedback.Status == FeedbackStatus.Failed ? "failed" : "retry scheduled")}", feedback.GameId);
        }
        catch (Exception ex)
        {
            _activity.Emit("err", $"{ShortId.Of(feedback.Id)} analysis error: {ex.Message}", feedback.GameId);
            feedback.Status = FeedbackStatus.Failed;
            feedback.LastErrorCode = "analysis_error";
            feedback.LastErrorMessage = Truncate(ex.Message);
            feedback.ProcessingLeaseUntil = null;
            feedback.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Permanent analysis failure for {Id}", feedback.Id);
        }
        return true;
    }

    private async Task StoreSuccess(AppDbContext db, Feedback feedback, FeedbackAnalysisResult r, CancellationToken ct)
    {
        // Replace any prior analysis (idempotent reprocessing).
        var existing = await db.Analyses.Include(a => a.Entities)
            .FirstOrDefaultAsync(a => a.FeedbackId == feedback.Id, ct);
        if (existing is not null) db.Analyses.Remove(existing);

        var manualReview = r.Confidence < _feedback.ManualReviewConfidenceThreshold || r.Conflicts.Count > 0;

        var analysis = new FeedbackAnalysis
        {
            FeedbackId = feedback.Id,
            SchemaVersion = r.SchemaVersion,
            PrimaryCategory = r.PrimaryCategory,
            Tags = r.Tags.ToList(),
            Severity = r.Severity,
            Toxicity = r.Toxicity,
            Sentiment = r.Sentiment,
            Summary = r.Summary,
            Confidence = r.Confidence,
            RequiresManualReview = manualReview,
            Provider = r.Provider,
            Model = r.Model,
            PromptVersion = r.PromptVersion,
            RawResponseHash = r.RawResponseHash,
            DurationMilliseconds = r.DurationMilliseconds,
            Entities = r.Entities.Select(e => new FeedbackEntity
            {
                GameId = feedback.GameId,
                Source = feedback.Source,
                Sentiment = r.Sentiment,
                Type = e.Type,
                Name = e.Name,
                NormalizedName = e.NormalizedName,
                Evidence = e.Evidence,
                Confidence = e.Confidence
            }).ToList()
        };
        db.Analyses.Add(analysis);

        feedback.Status = manualReview ? FeedbackStatus.ManualReview : FeedbackStatus.Completed;
        feedback.ProcessingLeaseUntil = null;
        feedback.NextAttemptAt = null;
        feedback.LastErrorCode = null;
        feedback.LastErrorMessage = null;
        feedback.UpdatedAt = DateTime.UtcNow;

        // A previously empty summary can become populated when the first matching analysis
        // completes, so invalidate both terminal data-bearing states.
        await db.Summaries
            .Where(s => s.GameId == feedback.GameId &&
                (s.Status == SummaryStatus.Ready || s.Status == SummaryStatus.Empty))
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, SummaryStatus.Invalidated)
                .SetProperty(s => s.InvalidatedAt, DateTime.UtcNow), ct);

        await db.SaveChangesAsync(ct);
    }

    private void HandleTransient(Feedback f, string code, string message)
    {
        if (f.AttemptCount >= RetrySchedule.MaxAttempts)
        {
            f.Status = FeedbackStatus.Failed;
            f.ProcessingLeaseUntil = null;
        }
        else
        {
            Reschedule(f, code, message);
        }
        f.LastErrorCode = code;
        f.LastErrorMessage = Truncate(message);
    }

    private static void Reschedule(Feedback f, string code, string message)
    {
        f.Status = FeedbackStatus.RetryScheduled;
        f.NextAttemptAt = RetrySchedule.NextAttempt(f.AttemptCount);
        f.ProcessingLeaseUntil = null;
        f.LastErrorCode = code;
        f.LastErrorMessage = Truncate(message);
        f.UpdatedAt = DateTime.UtcNow;
    }

    private static void ToManualReview(Feedback f, string code, string message)
    {
        f.Status = FeedbackStatus.ManualReview;
        f.ProcessingLeaseUntil = null;
        f.LastErrorCode = code;
        f.LastErrorMessage = Truncate(message);
        f.UpdatedAt = DateTime.UtcNow;
    }

    private static string Truncate(string s) => s.Length > 900 ? s[..900] : s;

    /// <summary>Claim one due feedback row using FOR UPDATE SKIP LOCKED.</summary>
    private async Task<Guid?> ClaimAsync(AppDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Include 'Processing' so items stranded by a crash/restart (lease expired) are recovered.
        // The lease guard means an actively-processing item is never stolen.
        var sql = @"
SELECT * FROM feedback
WHERE status IN ('Pending','RetryScheduled','Processing')
  AND (next_attempt_at IS NULL OR next_attempt_at <= now())
  AND (processing_lease_until IS NULL OR processing_lease_until < now())
ORDER BY created_at
FOR UPDATE SKIP LOCKED
LIMIT 1";
        var row = await db.Feedback.FromSqlRaw(sql).AsTracking().ToListAsync(ct);
        var f = row.FirstOrDefault();
        if (f is null) { await tx.RollbackAsync(ct); return null; }

        f.Status = FeedbackStatus.Processing;
        f.AttemptCount += 1;
        f.ProcessingLeaseUntil = DateTime.UtcNow.AddSeconds(_worker.LeaseSeconds);
        f.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return f.Id;
    }
}
