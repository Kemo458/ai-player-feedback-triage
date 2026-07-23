using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlayerFeedback.Core.Contracts;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;
using PlayerFeedback.Core.Scraping;

namespace PlayerFeedback.Api.Controllers;

[ApiController]
[Authorize]
public class ImportsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ImportsController(AppDbContext db) => _db = db;

    [HttpPost("api/games/{gameId:guid}/imports/google-play")]
    public async Task<ActionResult<ImportJobDto>> Start(Guid gameId, [FromBody] ImportRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game is null) return Problem(statusCode: 404, title: "Game not found.");

        if (!PlayStoreUrl.TryParsePackageId(request.Url, out var packageId, out var err))
            return Problem(statusCode: 400, title: err ?? "Invalid Google Play URL.");

        var count = request.Count;
        if (count is < 1 or > 500) return Problem(statusCode: 400, title: "count must be between 1 and 500.");
        var sort = request.Sort?.ToLowerInvariant() switch
        {
            "newest" => "newest",
            "mostrelevant" => "mostRelevant",
            _ => null
        };
        if (sort is null) return Problem(statusCode: 400, title: "sort must be 'newest' or 'mostRelevant'.");
        if (request.Score is < 1 or > 5) return Problem(statusCode: 400, title: "score must be null or 1..5.");
        if (request.Language.Length is < 2 or > 5) return Problem(statusCode: 400, title: "Invalid language.");
        if (request.Country.Length is < 2 or > 5) return Problem(statusCode: 400, title: "Invalid country.");

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.ImportJobs
                .FirstOrDefaultAsync(j => j.GameId == gameId && j.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                if (existing.PackageId != packageId || existing.RequestedCount != count)
                    return Problem(statusCode: 409, title: "Idempotency key reused with a different payload.");
                Response.Headers.Location = $"/api/imports/{existing.Id}";
                return Accepted(existing.ToDto());
            }
        }

        if (game.GooglePlayPackageId is null)
        {
            game.GooglePlayPackageId = packageId;
            game.GooglePlayUrl = PlayStoreUrl.Canonical(packageId);
            game.UpdatedAt = DateTime.UtcNow;
        }

        var job = new GooglePlayImportJob
        {
            GameId = gameId,
            PackageId = packageId,
            RequestedCount = count,
            Language = request.Language.ToLowerInvariant(),
            Country = request.Country.ToLowerInvariant(),
            Sort = sort,
            ScoreFilter = request.Score,
            Status = ImportStatus.Queued,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            CreatedByUserId = User.Identity?.Name
        };
        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync();

        Response.Headers.Location = $"/api/imports/{job.Id}";
        return Accepted(job.ToDto());
    }

    [HttpGet("api/imports/{importId:guid}")]
    public async Task<ActionResult<ImportJobDto>> Get(Guid importId)
    {
        var job = await _db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == importId);
        return job is null ? Problem(statusCode: 404, title: "Import not found.") : Ok(job.ToDto());
    }

    [HttpPost("api/imports/{importId:guid}/cancel")]
    public async Task<ActionResult> Cancel(Guid importId)
    {
        var job = await _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == importId);
        if (job is null) return Problem(statusCode: 404, title: "Import not found.");
        if (job.Status is ImportStatus.Completed or ImportStatus.Failed
            or ImportStatus.PartiallyCompleted or ImportStatus.Cancelled)
            return Accepted(job.ToDto());

        job.CancelRequested = true;
        if (job.Status == ImportStatus.Queued && job.ProcessingLeaseUntil is null)
        {
            job.Status = ImportStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return Accepted(job.ToDto());
    }
}
