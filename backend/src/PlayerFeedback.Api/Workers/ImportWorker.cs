using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlayerFeedback.Api.Activity;
using PlayerFeedback.Api.Hubs;
using PlayerFeedback.Core.Common;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;
using PlayerFeedback.Core.Scraping;

namespace PlayerFeedback.Api.Workers;

public class ImportWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly WorkerOptions _worker;
    private readonly IActivityLog _activity;
    private readonly ILogger<ImportWorker> _logger;

    public ImportWorker(IServiceProvider services, IOptions<WorkerOptions> worker,
        IActivityLog activity, ILogger<ImportWorker> logger)
    {
        _services = services;
        _worker = worker.Value;
        _activity = activity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked = false;
            try { worked = await ProcessOne(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Import worker loop error"); }
            if (!worked)
                await Task.Delay(_worker.PollIntervalMilliseconds, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private async Task<bool> ProcessOne(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scraper = scope.ServiceProvider.GetRequiredService<IGooglePlayScraper>();
        var notifier = scope.ServiceProvider.GetRequiredService<IFeedbackNotifier>();

        var jobId = await ClaimAsync(db, ct);
        if (jobId is null) return false;

        var job = await db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return true;

        if (job.CancelRequested)
        {
            job.Status = ImportStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.ProcessingLeaseUntil = null;
            await db.SaveChangesAsync(ct);
            await notifier.ImportProgressChanged(job.GameId, job.Id);
            return true;
        }

        job.Status = ImportStatus.Fetching;
        job.StartedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.ImportProgressChanged(job.GameId, job.Id);
        _activity.Emit("run", $"import {job.PackageId}: fetching up to {job.RequestedCount} reviews…", job.GameId);

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromMinutes(2)); // per-import budget

            var result = await scraper.FetchReviewsAsync(
                new ScrapeRequest(job.PackageId, job.RequestedCount, job.Language, job.Country, job.Sort, job.ScoreFilter),
                budget.Token);

            job.FetchedCount = result.ReturnedCount;
            job.Status = ImportStatus.Persisting;
            await db.SaveChangesAsync(ct);
            _activity.Emit("run", $"import {job.PackageId}: fetched {result.ReturnedCount}, saving…", job.GameId);

            foreach (var review in result.Reviews)
            {
                if (ct.IsCancellationRequested) break;
                await UpsertReview(db, job, review, ct);
            }

            job.Status = job.FailedCount > 0 ? ImportStatus.PartiallyCompleted : ImportStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.ProcessingLeaseUntil = null;
            await db.SaveChangesAsync(ct);
            await notifier.ImportProgressChanged(job.GameId, job.Id);
            _activity.Emit("ok", $"import {job.PackageId} {job.Status}: +{job.InsertedCount} new, {job.UpdatedCount} updated, {job.SkippedCount} skipped → queued for analysis", job.GameId);
            _logger.LogInformation("Import {Id} {Status}: fetched={F} inserted={I} updated={U} skipped={S}",
                job.Id, job.Status, job.FetchedCount, job.InsertedCount, job.UpdatedCount, job.SkippedCount);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            HandleTransient(job, "import_timeout", "Import exceeded its time budget.");
            await db.SaveChangesAsync(ct);
        }
        catch (ScraperException ex)
        {
            HandleTransient(job, "scraper_error", ex.Message);
            await db.SaveChangesAsync(ct);
            _activity.Emit("warn", $"import {job.PackageId} scraper error — {(job.Status == ImportStatus.Queued ? "retry scheduled" : job.Status.ToString())}", job.GameId);
        }
        catch (Exception ex)
        {
            _activity.Emit("err", $"import {job.PackageId} failed: {ex.Message}", job.GameId);
            job.Status = job.InsertedCount > 0 ? ImportStatus.PartiallyCompleted : ImportStatus.Failed;
            job.LastErrorCode = "import_error";
            job.LastErrorMessage = Truncate(ex.Message);
            job.CompletedAt = DateTime.UtcNow;
            job.ProcessingLeaseUntil = null;
            await db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Import {Id} failed", job.Id);
        }
        return true;
    }

    private static async Task UpsertReview(AppDbContext db, GooglePlayImportJob job, ScrapedReview review, CancellationToken ct)
    {
        var text = ContentHasher.Normalize(review.Text);
        if (string.IsNullOrWhiteSpace(text)) { job.SkippedCount++; return; }

        var hash = ContentHasher.Hash(text, review.Rating?.ToString(), review.AppVersion);
        var existing = await db.Feedback.FirstOrDefaultAsync(
            f => f.GameId == job.GameId && f.Source == FeedbackSource.GooglePlay && f.ExternalId == review.ExternalId, ct);

        if (existing is null)
        {
            db.Feedback.Add(new Feedback
            {
                GameId = job.GameId,
                Source = FeedbackSource.GooglePlay,
                ExternalId = review.ExternalId,
                AuthorName = Author(review.Author),
                Text = text,
                ContentHash = hash,
                Rating = review.Rating,
                AppVersion = review.AppVersion,
                SourceCreatedAt = review.CreatedAt,
                ImportedAt = DateTime.UtcNow,
                Status = FeedbackStatus.Pending
            });
            job.InsertedCount++;
        }
        else if (existing.ContentHash != hash)
        {
            existing.Text = text;
            existing.ContentHash = hash;
            existing.Rating = review.Rating;
            existing.AuthorName = Author(review.Author);
            existing.AppVersion = review.AppVersion;
            existing.Status = FeedbackStatus.Pending;
            existing.NextAttemptAt = null;
            existing.UpdatedAt = DateTime.UtcNow;
            job.UpdatedCount++;
        }
        else
        {
            job.SkippedCount++;
        }
        await db.SaveChangesAsync(ct);
    }

    private static void HandleTransient(GooglePlayImportJob job, string code, string message)
    {
        job.LastErrorCode = code;
        job.LastErrorMessage = Truncate(message);
        if (job.AttemptCount >= RetrySchedule.MaxAttempts)
        {
            job.Status = job.InsertedCount > 0 ? ImportStatus.PartiallyCompleted : ImportStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.ProcessingLeaseUntil = null;
        }
        else
        {
            job.Status = ImportStatus.Queued;
            job.NextAttemptAt = RetrySchedule.NextAttempt(job.AttemptCount);
            job.ProcessingLeaseUntil = null;
        }
    }

    private static string Truncate(string s) => s.Length > 900 ? s[..900] : s;

    private static string? Author(string? a)
    {
        if (string.IsNullOrWhiteSpace(a)) return null;
        var t = a.Trim();
        return t.Length > 120 ? t[..120] : t;
    }

    private async Task<Guid?> ClaimAsync(AppDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Also recover jobs stranded mid-run (Fetching/Persisting) once their lease has expired,
        // e.g. after a crash or a poisoned save. The lease guard prevents stealing active jobs.
        var sql = @"
SELECT * FROM import_job
WHERE status IN ('Queued','Fetching','Persisting')
  AND (next_attempt_at IS NULL OR next_attempt_at <= now())
  AND (processing_lease_until IS NULL OR processing_lease_until < now())
ORDER BY created_at
FOR UPDATE SKIP LOCKED
LIMIT 1";
        var rows = await db.ImportJobs.FromSqlRaw(sql).AsTracking().ToListAsync(ct);
        var job = rows.FirstOrDefault();
        if (job is null) { await tx.RollbackAsync(ct); return null; }

        job.AttemptCount += 1;
        job.ProcessingLeaseUntil = DateTime.UtcNow.AddSeconds(_worker.LeaseSeconds * 2);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return job.Id;
    }
}
