using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlayerFeedback.Api.Auth;
using PlayerFeedback.Core.Common;
using PlayerFeedback.Core.Contracts;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Api.Controllers;

[ApiController]
[AllowAnonymous]
public class PublicFeedbackController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuthOptions _auth;

    public PublicFeedbackController(AppDbContext db, IOptions<AuthOptions> auth)
    {
        _db = db;
        _auth = auth.Value;
    }

    [HttpPost("api/public/games/{gameId:guid}/feedback")]
    public async Task<ActionResult<AcceptedDto>> Submit(
        Guid gameId,
        [FromBody] PublicFeedbackRequest request,
        [FromHeader(Name = "X-Submission-Token")] string? submissionToken)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game is null || !game.SubmissionEnabled)
            return Problem(statusCode: 404, title: "Game not found or submissions disabled.");

        var canonicalToken = TokenGenerator.StableForGame(gameId, _auth.JwtSigningKey);
        var submittedHash = string.IsNullOrWhiteSpace(submissionToken)
            ? null
            : TokenGenerator.HashToken(submissionToken);
        var canonicalHash = TokenGenerator.HashToken(canonicalToken);
        var matchesCanonical = submittedHash == canonicalHash;
        var matchesLegacy = game.SubmissionTokenHash is not null &&
                            submittedHash == game.SubmissionTokenHash;

        if (!matchesCanonical && !matchesLegacy)
            return Problem(statusCode: 401, title: "Invalid submission token.");

        var text = ContentHasher.Normalize(request.Text ?? "");
        if (text.Length is < 3 or > 5000)
            return Problem(statusCode: 400, title: "Feedback text must be between 3 and 5000 characters.");
        if (request.Rating is < 1 or > 5)
            return Problem(statusCode: 400, title: "Rating must be null or 1..5.");

        var feedback = new Feedback
        {
            GameId = gameId,
            Source = FeedbackSource.Internal,
            Text = text,
            ContentHash = ContentHasher.Hash(text, request.Rating?.ToString()),
            Rating = request.Rating,
            AppVersion = Trim(request.AppVersion, 50),
            Device = Trim(request.Device, 100),
            Locale = Trim(request.Locale, 20),
            SourceCreatedAt = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow,
            Status = FeedbackStatus.Pending
        };
        _db.Feedback.Add(feedback);
        await _db.SaveChangesAsync();

        Response.Headers.Location = $"/api/public/feedback/{feedback.Id}/status";
        return Accepted(new AcceptedDto(feedback.Id, feedback.Status.ToString()));
    }

    [HttpGet("api/public/feedback/{feedbackId:guid}/status")]
    public async Task<ActionResult<AcceptedDto>> Status(Guid feedbackId)
    {
        var f = await _db.Feedback.AsNoTracking()
            .Where(x => x.Id == feedbackId)
            .Select(x => new { x.Id, x.Status })
            .FirstOrDefaultAsync();
        if (f is null) return Problem(statusCode: 404, title: "Not found.");

        // Coarse state only — never expose analysis details or other users' feedback.
        var coarse = f.Status switch
        {
            FeedbackStatus.Completed => "Processed",
            FeedbackStatus.Failed => "Failed",
            FeedbackStatus.ManualReview => "UnderReview",
            _ => "Processing"
        };
        return Ok(new AcceptedDto(f.Id, coarse));
    }

    private static string? Trim(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length > max ? s[..max] : s);
}
