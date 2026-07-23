using System.Text.Json;
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
[Route("api/games/{gameId:guid}")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly int _concurrency;
    public DashboardController(AppDbContext db, IOptions<WorkerOptions> worker)
    {
        _db = db;
        _concurrency = Math.Max(1, worker.Value.AnalysisConcurrency);
    }

    [HttpGet("pipeline")]
    public async Task<ActionResult> Pipeline(Guid gameId)
    {
        if (!await _db.Games.AnyAsync(g => g.Id == gameId))
            return Problem(statusCode: 404, title: "Game not found.");

        var counts = (await _db.Feedback.AsNoTracking().Where(f => f.GameId == gameId)
            .GroupBy(f => f.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.C);
        int C(FeedbackStatus s) => counts.GetValueOrDefault(s);

        var imported = counts.Values.Sum();
        var queued = C(FeedbackStatus.Pending) + C(FeedbackStatus.RetryScheduled);
        var analyzing = C(FeedbackStatus.Processing);
        var done = C(FeedbackStatus.Completed) + C(FeedbackStatus.ManualReview);
        var failed = C(FeedbackStatus.Failed);

        var globalQueued = await _db.Feedback.CountAsync(f =>
            f.Status == FeedbackStatus.Pending || f.Status == FeedbackStatus.RetryScheduled);
        var globalAnalyzing = await _db.Feedback.CountAsync(f => f.Status == FeedbackStatus.Processing);

        var avgMs = await _db.Analyses.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).Take(20)
            .Select(a => (double?)a.DurationMilliseconds).AverageAsync() ?? 18000.0;
        var avgSec = Math.Max(1, (int)Math.Round(avgMs / 1000.0));
        var throughputPerMin = Math.Round(_concurrency * 60.0 / avgSec, 1);
        var remaining = queued + analyzing;
        var etaSeconds = throughputPerMin > 0 ? (int)Math.Round(remaining / throughputPerMin * 60.0) : 0;

        return Ok(new
        {
            imported, queued, analyzing, done, failed,
            globalQueued, globalAnalyzing,
            throughputPerMin, avgItemSeconds = avgSec, concurrency = _concurrency,
            etaSeconds
        });
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard(Guid gameId)
    {
        if (!await _db.Games.AnyAsync(g => g.Id == gameId))
            return Problem(statusCode: 404, title: "Game not found.");

        var feedback = _db.Feedback.AsNoTracking().Where(f => f.GameId == gameId);

        var bySource = await feedback.GroupBy(f => f.Source)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var byStatus = (await feedback.GroupBy(f => f.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);
        var ratings = await feedback.Where(f => f.Rating != null)
            .GroupBy(f => f.Rating!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

        var analyses = _db.Analyses.AsNoTracking().Where(a => a.Feedback!.GameId == gameId);
        var byCategory = await analyses.GroupBy(a => a.PrimaryCategory)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var bySeverity = await analyses.GroupBy(a => a.Severity)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var bySentiment = await analyses.GroupBy(a => a.Sentiment)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();

        var criticalBugs = await analyses.CountAsync(a => a.Tags.Contains("Bug") && a.Severity == Severity.Critical);
        var toxic = await analyses.CountAsync(a => a.Toxicity == Toxicity.Toxic || a.Tags.Contains("Toxic"));

        var topEntitiesRaw = await _db.Entities.AsNoTracking().Where(e => e.GameId == gameId)
            .GroupBy(e => new { e.NormalizedName, e.Type })
            .Select(g => new { g.Key.NormalizedName, g.Key.Type, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(10).ToListAsync();
        var topEntities = topEntitiesRaw
            .Select(x => new TopEntity(x.NormalizedName, x.Type.ToString(), x.Count)).ToList();

        var totals = new Totals(
            bySource.Sum(x => x.Count),
            EnumDict<FeedbackSource>(s => bySource.FirstOrDefault(x => x.Key == s)?.Count ?? 0));

        var processing = new ProcessingCounts(
            byStatus.GetValueOrDefault(FeedbackStatus.Pending),
            byStatus.GetValueOrDefault(FeedbackStatus.Processing),
            byStatus.GetValueOrDefault(FeedbackStatus.Completed),
            byStatus.GetValueOrDefault(FeedbackStatus.RetryScheduled),
            byStatus.GetValueOrDefault(FeedbackStatus.ManualReview),
            byStatus.GetValueOrDefault(FeedbackStatus.Failed));

        var ratingDist = new Dictionary<string, int>();
        for (int i = 1; i <= 5; i++) ratingDist[i.ToString()] = ratings.FirstOrDefault(r => r.Key == i)?.Count ?? 0;

        var dto = new DashboardDto(
            totals, processing,
            EnumDict<PrimaryCategory>(c => byCategory.FirstOrDefault(x => x.Key == c)?.Count ?? 0),
            EnumDict<Severity>(s => bySeverity.FirstOrDefault(x => x.Key == s)?.Count ?? 0),
            EnumDict<Sentiment>(s => bySentiment.FirstOrDefault(x => x.Key == s)?.Count ?? 0),
            criticalBugs, toxic,
            processing.Failed + processing.ManualReview,
            topEntities, ratingDist);
        return Ok(dto);
    }

    [HttpGet("summaries")]
    public async Task<ActionResult<SummaryDto>> GetSummary(Guid gameId,
        [FromQuery] string? source, [FromQuery] string? tag,
        [FromQuery] string? severity, [FromQuery] string? sentiment)
    {
        if (!await _db.Games.AnyAsync(g => g.Id == gameId))
            return Problem(statusCode: 404, title: "Game not found.");

        var (scopeKey, s) = await GetOrCreateScope(gameId, source, tag, severity, sentiment, resetToPending: false);
        return Ok(ToDto(s));
    }

    [HttpPost("summaries/refresh")]
    public async Task<ActionResult<SummaryDto>> Refresh(Guid gameId,
        [FromQuery] string? source, [FromQuery] string? tag,
        [FromQuery] string? severity, [FromQuery] string? sentiment)
    {
        if (!await _db.Games.AnyAsync(g => g.Id == gameId))
            return Problem(statusCode: 404, title: "Game not found.");

        var (_, s) = await GetOrCreateScope(gameId, source, tag, severity, sentiment, resetToPending: true);
        return Accepted(ToDto(s));
    }

    [HttpGet("entities")]
    public async Task<ActionResult<Paged<EntityAggregateDto>>> Entities(Guid gameId,
        [FromQuery] string? type, [FromQuery] string? source)
    {
        var q = _db.Entities.AsNoTracking().Where(e => e.GameId == gameId);
        if (Enum.TryParse<EntityType>(type, true, out var et) && !string.IsNullOrWhiteSpace(type))
            q = q.Where(e => e.Type == et);
        if (Enum.TryParse<FeedbackSource>(source, true, out var src) && !string.IsNullOrWhiteSpace(source))
            q = q.Where(e => e.Source == src);

        var rows = await q.Select(e => new
        {
            e.NormalizedName, e.Type, e.Source, e.Sentiment, e.Evidence, e.AnalysisId
        }).ToListAsync();

        var items = rows.GroupBy(e => new { e.NormalizedName, e.Type })
            .Select(g => new EntityAggregateDto(
                g.Key.NormalizedName,
                g.Key.Type.ToString(),
                g.Count(),
                g.Select(x => x.AnalysisId).Distinct().Count(),
                g.GroupBy(x => x.Source.ToString()).ToDictionary(k => k.Key, v => v.Count()),
                g.GroupBy(x => x.Sentiment.ToString()).ToDictionary(k => k.Key, v => v.Count()),
                g.Select(x => x.Evidence).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().Take(3).ToList()))
            .OrderByDescending(x => x.MentionCount)
            .Take(100)
            .ToList();

        return Ok(new Paged<EntityAggregateDto>(items, null));
    }

    // ---- helpers ----

    private async Task<(string, AggregateSummary)> GetOrCreateScope(
        Guid gameId, string? source, string? tag, string? severity, string? sentiment, bool resetToPending)
    {
        var src = Norm(source, typeof(FeedbackSource));
        var sev = Norm(severity, typeof(Severity));
        var sen = Norm(sentiment, typeof(Sentiment));
        var tg = NormTag(tag);
        var scopeKey = $"{src}|{tg}|{sev}|{sen}";

        var s = await _db.Summaries.FirstOrDefaultAsync(x => x.GameId == gameId && x.ScopeKey == scopeKey);
        if (s is null)
        {
            s = new AggregateSummary
            {
                GameId = gameId, ScopeKey = scopeKey,
                SourceFilter = src, CategoryFilter = tg, SeverityFilter = sev, SentimentFilter = sen,
                Status = SummaryStatus.Pending, PromptVersion = "feedback-analysis-v1"
            };
            _db.Summaries.Add(s);
            await _db.SaveChangesAsync();
        }
        else if (resetToPending ||
            s.Status == SummaryStatus.Invalidated ||
            s.Status == SummaryStatus.Failed)
        {
            s.Status = SummaryStatus.Pending;
            s.ProcessingLeaseUntil = null;
            s.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        else if (s.Status == SummaryStatus.Empty)
        {
            // Recover summaries that were generated before an import finished. Older
            // deployments only invalidated Ready summaries when analysis completed, which
            // could otherwise leave a populated scope permanently marked Empty.
            var filter = new FeedbackFilter(
                Source: string.IsNullOrWhiteSpace(src) ? null : src,
                Tag: string.IsNullOrWhiteSpace(tg) ? null : tg,
                Severity: string.IsNullOrWhiteSpace(sev) ? null : sev,
                Sentiment: string.IsNullOrWhiteSpace(sen) ? null : sen);
            var hasAnalyzedFeedback = await FeedbackQuery
                .Apply(_db.Feedback.AsNoTracking(), gameId, filter)
                .AnyAsync(f => f.Analysis != null);
            if (hasAnalyzedFeedback)
            {
                s.Status = SummaryStatus.Pending;
                s.ProcessingLeaseUntil = null;
                s.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        return (scopeKey, s);
    }

    private static SummaryDto ToDto(AggregateSummary s)
    {
        List<SummaryThemeDto> themes;
        try
        {
            themes = JsonSerializer.Deserialize<List<SummaryThemeDto>>(s.ThemesJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
        }
        catch { themes = new(); }
        return new SummaryDto(s.Id, s.Status.ToString(), s.Overview, themes,
            s.IncludedFeedbackCount, s.GeneratedAt, s.InvalidatedAt, s.Provider, s.Model);
    }

    private static string Norm(string? value, Type enumType) =>
        !string.IsNullOrWhiteSpace(value) && Enum.IsDefined(enumType, Capitalize(value)) ? Capitalize(value) : "";

    private static string NormTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "";
        var t = Capitalize(tag);
        return t is "Bug" or "Feature" or "Lore" or "Toxic" ? t : "";
    }

    private static string Capitalize(string v) =>
        v.Length == 0 ? v : char.ToUpperInvariant(v[0]) + v[1..].ToLowerInvariant();

    private static Dictionary<string, int> EnumDict<TEnum>(Func<TEnum, int> selector) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().ToDictionary(e => e.ToString(), e => selector(e));
}
