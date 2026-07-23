using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlayerFeedback.Api.Activity;
using PlayerFeedback.Api.Hubs;
using PlayerFeedback.Core.Analysis;
using PlayerFeedback.Core.Common;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Api.Workers;

public class SummaryWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly WorkerOptions _worker;
    private readonly IActivityLog _activity;
    private readonly ILogger<SummaryWorker> _logger;

    public SummaryWorker(
        IServiceProvider services,
        IOptions<WorkerOptions> worker,
        IActivityLog activity,
        ILogger<SummaryWorker> logger)
    {
        _services = services;
        _worker = worker.Value;
        _activity = activity;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A Generating row belongs to the previous process after an API restart. Recover it
        // immediately instead of leaving the UI behind a multi-minute lease.
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Summaries
                .Where(summary =>
                    summary.Status == SummaryStatus.Generating ||
                    summary.Status == SummaryStatus.Failed)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(summary => summary.Status, SummaryStatus.Pending)
                    .SetProperty(summary => summary.ProcessingLeaseUntil, (DateTime?)null)
                    .SetProperty(summary => summary.UpdatedAt, DateTime.UtcNow), stoppingToken);
        }

        // Small debounce so a large import doesn't regenerate after every item.
        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked = false;
            try { worked = await ProcessOne(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Summary worker loop error"); }
            await Task.Delay(worked ? 500 : 2000, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private async Task<bool> ProcessOne(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var summarizer = scope.ServiceProvider.GetRequiredService<IAggregateSummarizer>();
        var notifier = scope.ServiceProvider.GetRequiredService<IFeedbackNotifier>();

        var id = await ClaimAsync(db, ct);
        if (id is null) return false;

        var summary = await db.Summaries.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (summary is null) return true;

        var filter = new FeedbackFilter(
            Source: Nz(summary.SourceFilter), Tag: Nz(summary.CategoryFilter),
            Severity: Nz(summary.SeverityFilter), Sentiment: Nz(summary.SentimentFilter));

        var query = FeedbackQuery.Apply(db.Feedback.AsNoTracking(), summary.GameId, filter)
            .Where(f => f.Analysis != null)
            .OrderByDescending(f => f.SourceCreatedAt ?? f.ImportedAt);

        var summaries = await query.Take(200).Select(f => f.Analysis!.Summary).ToListAsync(ct);
        var total = await query.CountAsync(ct);
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == summary.GameId, ct);
        var scopeDesc = DescribeScope(summary);
        var gameName = game?.Name ?? "Unknown";

        if (summaries.Count == 0)
        {
            summary.Status = SummaryStatus.Empty;
            summary.Overview = "No completed feedback matches this scope yet.";
            summary.ThemesJson = "[]";
            summary.IncludedFeedbackCount = 0;
            summary.GeneratedAt = DateTime.UtcNow;
            summary.ProcessingLeaseUntil = null;
            summary.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await notifier.SummaryUpdated(summary.GameId, summary.Id);
            _activity.Emit("ok", $"summary · {gameName} · {scopeDesc}: no matching feedback", summary.GameId);
            return true;
        }

        _activity.Emit("run",
            $"summarizing {total} items · {gameName} · {scopeDesc} → {summarizer.Provider} {summarizer.Model}",
            summary.GameId);

        SummaryResult result;
        var analysisBusy = await db.Feedback.AsNoTracking().AnyAsync(feedback =>
            feedback.Status == FeedbackStatus.Pending ||
            feedback.Status == FeedbackStatus.Processing ||
            feedback.Status == FeedbackStatus.RetryScheduled, ct);
        if (analysisBusy && summarizer.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            result = await FastFallback(gameName, scopeDesc, total, summaries, ct);
            _activity.Emit("warn",
                $"summary · {gameName}: Qwen workers busy, published fast fallback",
                summary.GameId);
        }
        else
        {
            try
            {
                result = await summarizer.SummarizeAsync(
                    new SummaryInput(gameName, scopeDesc, total, summaries), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A dashboard summary is an aid, not a durable job worth blocking the UI for.
                // Publish a transparent deterministic fallback and allow a later Refresh to
                // upgrade it with Qwen when capacity is available.
                result = await FastFallback(gameName, scopeDesc, total, summaries, CancellationToken.None);
                _activity.Emit("warn",
                    $"summary · {gameName}: Qwen unavailable, published fast fallback",
                    summary.GameId);
                _logger.LogWarning(ex, "Qwen summary failed for {Id}; used fast fallback", summary.Id);
            }
        }

        summary.Overview = result.Overview.Length > 3900 ? result.Overview[..3900] : result.Overview;
        summary.ThemesJson = JsonSerializer.Serialize(result.Themes.Select(t => new { name = t.Name, count = t.Count }));
        summary.IncludedFeedbackCount = total;
        summary.InputFingerprint = ContentHasher.Hash(summaries.ToArray());
        summary.Provider = result.Provider;
        summary.Model = result.Model;
        summary.Status = SummaryStatus.Ready;
        summary.GeneratedAt = DateTime.UtcNow;
        summary.InvalidatedAt = null;
        summary.ProcessingLeaseUntil = null;
        summary.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.SummaryUpdated(summary.GameId, summary.Id);
        _activity.Emit(result.Provider == "FastFallback" ? "warn" : "ok",
            $"summary ready · {gameName} · {total} items · {result.Provider} {result.Model}",
            summary.GameId);
        _logger.LogInformation("Generated summary {Id} ({Count} items)", summary.Id, total);
        return true;
    }

    private static async Task<SummaryResult> FastFallback(
        string gameName,
        string scopeDesc,
        int total,
        IReadOnlyList<string> summaries,
        CancellationToken ct)
    {
        var fallback = await new MockAggregateSummarizer().SummarizeAsync(
            new SummaryInput(gameName, scopeDesc, total, summaries), ct);
        var overview = fallback.Overview.Replace(
            "This is a deterministic offline summary; enable the LLM provider for narrative synthesis.",
            "This fast summary keeps the dashboard useful while Qwen workers are busy; refresh later for narrative synthesis.",
            StringComparison.Ordinal);
        return fallback with
        {
            Overview = overview,
            Provider = "FastFallback",
            Model = "queue-aware-v1"
        };
    }

    private static string? Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string DescribeScope(AggregateSummary s)
    {
        var parts = new List<string>();
        parts.Add(string.IsNullOrWhiteSpace(s.SourceFilter) ? "all sources" : s.SourceFilter);
        if (!string.IsNullOrWhiteSpace(s.CategoryFilter)) parts.Add($"tag={s.CategoryFilter}");
        if (!string.IsNullOrWhiteSpace(s.SeverityFilter)) parts.Add($"severity={s.SeverityFilter}");
        if (!string.IsNullOrWhiteSpace(s.SentimentFilter)) parts.Add($"sentiment={s.SentimentFilter}");
        return string.Join(", ", parts);
    }

    private async Task<Guid?> ClaimAsync(AppDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sql = @"
SELECT * FROM aggregate_summary
WHERE status IN ('Pending','Generating')
  AND (processing_lease_until IS NULL OR processing_lease_until < now())
ORDER BY CASE WHEN scope_key = '|||' THEN 0 ELSE 1 END, updated_at
FOR UPDATE SKIP LOCKED
LIMIT 1";
        var rows = await db.Summaries.FromSqlRaw(sql).AsTracking().ToListAsync(ct);
        var s = rows.FirstOrDefault();
        if (s is null) { await tx.RollbackAsync(ct); return null; }

        s.Status = SummaryStatus.Generating;
        s.ProcessingLeaseUntil = DateTime.UtcNow.AddSeconds(_worker.LeaseSeconds * 2);
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return s.Id;
    }
}
