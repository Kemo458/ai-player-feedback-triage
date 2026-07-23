using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlayerFeedback.Api.Workers;
using PlayerFeedback.Core.Contracts;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Api.Controllers;

[ApiController]
[Authorize]
public class FeedbackController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly int _concurrency;
    public FeedbackController(AppDbContext db, IOptions<WorkerOptions> worker)
    {
        _db = db;
        _concurrency = Math.Max(1, worker.Value.AnalysisConcurrency);
    }

    [HttpGet("api/games/{gameId:guid}/feedback")]
    public async Task<ActionResult<Paged<FeedbackDto>>> List(
        Guid gameId,
        [FromQuery] string? source, [FromQuery] string? tag, [FromQuery] string? severity,
        [FromQuery] string? sentiment, [FromQuery] string? status, [FromQuery] string? entity,
        [FromQuery] string? search, [FromQuery] string? sort,
        [FromQuery] string? cursor, [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        var offset = OffsetCursor.Decode(cursor);
        var filter = new FeedbackFilter(source, tag, severity, sentiment, status, entity, search);

        var query = FeedbackQuery.Apply(
            _db.Feedback.AsNoTracking().Include(f => f.Analysis).ThenInclude(a => a!.Entities),
            gameId, filter);

        query = sort switch
        {
            "-importedAt" => query.OrderByDescending(f => f.ImportedAt).ThenByDescending(f => f.Id),
            "importedAt" => query.OrderBy(f => f.ImportedAt).ThenBy(f => f.Id),
            "-rating" => query.OrderByDescending(f => f.Rating.HasValue)
                .ThenByDescending(f => f.Rating).ThenByDescending(f => f.ImportedAt),
            "rating" => query.OrderByDescending(f => f.Rating.HasValue)
                .ThenBy(f => f.Rating).ThenByDescending(f => f.ImportedAt),
            "confidence" => query.OrderBy(f => f.Analysis == null)
                .ThenBy(f => f.Analysis!.Confidence).ThenByDescending(f => f.ImportedAt),
            "priority" => query
                .OrderBy(f => f.Status == FeedbackStatus.Failed ? 0
                    : f.Status == FeedbackStatus.ManualReview ? 1
                    : f.Analysis != null && f.Analysis.Severity == Severity.Critical ? 2
                    : f.Analysis != null && f.Analysis.Severity == Severity.High ? 3
                    : 4)
                .ThenByDescending(f => f.ImportedAt),
            _ => query.OrderByDescending(f => f.SourceCreatedAt ?? f.ImportedAt).ThenByDescending(f => f.Id)
        };

        var items = await query.Skip(offset).Take(limit + 1).ToListAsync();
        string? next = items.Count > limit ? OffsetCursor.Encode(offset + limit) : null;
        return Ok(new Paged<FeedbackDto>(items.Take(limit).Select(f => f.ToDto()).ToList(), next));
    }

    [HttpGet("api/feedback/{feedbackId:guid}")]
    public async Task<ActionResult<FeedbackDto>> Get(Guid feedbackId)
    {
        var f = await _db.Feedback.AsNoTracking()
            .Include(x => x.Analysis).ThenInclude(a => a!.Entities)
            .FirstOrDefaultAsync(x => x.Id == feedbackId);
        return f is null ? Problem(statusCode: 404, title: "Feedback not found.") : Ok(f.ToDto());
    }

    /// <summary>Queue position + ETA for an item still awaiting analysis (global, oldest-first).</summary>
    [HttpGet("api/feedback/{feedbackId:guid}/queue")]
    public async Task<ActionResult> QueuePosition(Guid feedbackId)
    {
        var f = await _db.Feedback.AsNoTracking()
            .Where(x => x.Id == feedbackId)
            .Select(x => new { x.Status, x.CreatedAt })
            .FirstOrDefaultAsync();
        if (f is null) return Problem(statusCode: 404, title: "Feedback not found.");

        // The worker claims oldest-first, so anything not-yet-done with an earlier created_at runs before this.
        var ahead = await _db.Feedback.CountAsync(x =>
            (x.Status == FeedbackStatus.Pending || x.Status == FeedbackStatus.RetryScheduled
             || x.Status == FeedbackStatus.Processing)
            && x.CreatedAt < f.CreatedAt);

        var globalPending = await _db.Feedback.CountAsync(x =>
            x.Status == FeedbackStatus.Pending || x.Status == FeedbackStatus.RetryScheduled);
        var globalProcessing = await _db.Feedback.CountAsync(x => x.Status == FeedbackStatus.Processing);

        // Average recent analysis duration for a rough ETA (fallback 18s).
        var avgMs = await _db.Analyses.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).Take(20)
            .Select(a => (double?)a.DurationMilliseconds).AverageAsync() ?? 18000.0;
        var avgSec = Math.Max(1, (int)Math.Round(avgMs / 1000.0));
        // N items analyzed in parallel clear the line ~N times faster.
        var estWait = f.Status == FeedbackStatus.Processing ? 0 : (int)Math.Round((double)ahead * avgSec / _concurrency);

        return Ok(new
        {
            status = f.Status.ToString(),
            aheadCount = ahead,
            position = ahead + 1,
            estimatedWaitSeconds = estWait,
            avgItemSeconds = avgSec,
            concurrency = _concurrency,
            globalPending,
            globalProcessing
        });
    }

    [HttpPost("api/feedback/{feedbackId:guid}/retry")]
    public async Task<ActionResult> Retry(Guid feedbackId)
    {
        var f = await _db.Feedback.FirstOrDefaultAsync(x => x.Id == feedbackId);
        if (f is null) return Problem(statusCode: 404, title: "Feedback not found.");
        if (f.Status == FeedbackStatus.Processing)
            return Problem(statusCode: 409, title: "Feedback is already processing.");

        f.Status = FeedbackStatus.Pending;
        f.NextAttemptAt = null;
        f.ProcessingLeaseUntil = null;
        f.LastErrorCode = null;
        f.LastErrorMessage = null;
        f.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Accepted(f.ToDto());
    }

    [HttpPost("api/feedback/{feedbackId:guid}/mark-reviewed")]
    public async Task<ActionResult> MarkReviewed(Guid feedbackId)
    {
        var f = await _db.Feedback.Include(x => x.Analysis)
            .FirstOrDefaultAsync(x => x.Id == feedbackId);
        if (f is null) return Problem(statusCode: 404, title: "Feedback not found.");

        if (f.Analysis is not null) f.Analysis.RequiresManualReview = false;
        if (f.Status == FeedbackStatus.ManualReview) f.Status = FeedbackStatus.Completed;
        f.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(f.ToDto());
    }
}
