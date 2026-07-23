using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PlayerFeedback.Api.Auth;
using PlayerFeedback.Core.Common;
using PlayerFeedback.Core.Contracts;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;
using PlayerFeedback.Core.Scraping;

namespace PlayerFeedback.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/games")]
public class GamesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IGooglePlayScraper _scraper;
    private readonly IMemoryCache _cache;
    private readonly AuthOptions _auth;

    public GamesController(
        AppDbContext db,
        IGooglePlayScraper scraper,
        IMemoryCache cache,
        IOptions<AuthOptions> auth)
    {
        _db = db;
        _scraper = scraper;
        _cache = cache;
        _auth = auth.Value;
    }

    [HttpGet]
    public async Task<ActionResult<Paged<GameDto>>> List([FromQuery] string? cursor, [FromQuery] int limit = 25)
    {
        limit = Math.Clamp(limit, 1, 100);
        var offset = OffsetCursor.Decode(cursor);
        var items = await _db.Games.AsNoTracking()
            .OrderByDescending(g => g.CreatedAt).ThenBy(g => g.Id)
            .Skip(offset).Take(limit + 1).ToListAsync();
        string? next = items.Count > limit ? OffsetCursor.Encode(offset + limit) : null;
        var page = items.Take(limit).ToList();
        var dtos = await Task.WhenAll(page.Select(async game =>
            game.ToDto(iconUrl: await GetIconUrl(game.GooglePlayPackageId, HttpContext.RequestAborted))));
        return Ok(new Paged<GameDto>(dtos, next));
    }

    [HttpPost]
    public async Task<ActionResult<GameDto>> Create([FromBody] CreateGameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Problem(statusCode: 400, title: "Name is required.");

        string? packageId = null;
        string? canonical = null;
        if (!string.IsNullOrWhiteSpace(request.GooglePlayUrl))
        {
            if (!PlayStoreUrl.TryParsePackageId(request.GooglePlayUrl, out var pkg, out var err))
                return Problem(statusCode: 400, title: err ?? "Invalid Google Play URL.");
            packageId = pkg;
            canonical = PlayStoreUrl.Canonical(pkg);

            if (await _db.Games.AnyAsync(g => g.GooglePlayPackageId == packageId))
                return Problem(statusCode: 409, title: "A game with this Google Play package already exists.");
        }

        var game = new Game
        {
            Name = request.Name.Trim(),
            GooglePlayPackageId = packageId,
            GooglePlayUrl = canonical,
            SubmissionTokenHash = null,
            SubmissionEnabled = true
        };
        _db.Games.Add(game);

        // If a Google Play URL was given, immediately queue an import of the newest 100 reviews
        // so creating the game "starts digging" straight away.
        if (packageId is not null)
        {
            _db.ImportJobs.Add(new GooglePlayImportJob
            {
                GameId = game.Id,
                PackageId = packageId,
                RequestedCount = 100,
                Language = "en",
                Country = "us",
                Sort = "newest",
                Status = ImportStatus.Queued,
                CreatedByUserId = User.Identity?.Name
            });
        }

        await _db.SaveChangesAsync();

        return CreatedAtAction(
            nameof(Get),
            new { gameId = game.Id },
            game.ToDto(submissionToken: SubmissionTokenFor(game.Id)));
    }

    /// <summary>
    /// Retrieve the game's canonical internal-feedback token. This endpoint is
    /// retained for client compatibility; it never rotates the link.
    /// </summary>
    [HttpPost("{gameId:guid}/submission-token")]
    public async Task<ActionResult<GameDto>> GenerateSubmissionToken(Guid gameId)
    {
        var game = await _db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId);
        if (game is null) return Problem(statusCode: 404, title: "Game not found.");

        return Ok(game.ToDto(submissionToken: SubmissionTokenFor(game.Id)));
    }

    [HttpGet("{gameId:guid}")]
    public async Task<ActionResult<GameDto>> Get(Guid gameId)
    {
        var game = await _db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId);
        return game is null
            ? Problem(statusCode: 404, title: "Game not found.")
            : Ok(game.ToDto(submissionToken: SubmissionTokenFor(game.Id)));
    }

    /// <summary>Permanently delete a game and all its feedback, analyses, imports, and summaries.</summary>
    [HttpDelete("{gameId:guid}")]
    public async Task<IActionResult> Delete(Guid gameId)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game is null) return Problem(statusCode: 404, title: "Game not found.");

        // Summaries have no cascade relationship; remove them explicitly. Removing the game
        // cascades feedback -> analysis -> entities, and import jobs (configured FKs).
        await _db.Summaries.Where(s => s.GameId == gameId).ExecuteDeleteAsync();
        _db.Games.Remove(game);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string?> GetIconUrl(string? packageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(packageId)) return null;
        try
        {
            return await _cache.GetOrCreateAsync($"google-play-icon:{packageId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(TimeSpan.FromSeconds(8));
                var metadata = await _scraper.FetchAppMetadataAsync(packageId, budget.Token);
                return metadata.IconUrl;
            });
        }
        catch
        {
            // Artwork must never make the game picker unavailable.
            return null;
        }
    }

    private string SubmissionTokenFor(Guid gameId) =>
        TokenGenerator.StableForGame(gameId, _auth.JwtSigningKey);
}
