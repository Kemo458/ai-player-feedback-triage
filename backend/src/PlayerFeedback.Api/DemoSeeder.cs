using PlayerFeedback.Core.Common;
using PlayerFeedback.Core.Data;
using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Api;

/// <summary>Seeds a demo game + sample internal feedback on first boot (idempotent).</summary>
public static class DemoSeeder
{
    public const string DemoSubmissionToken = "demo-submission-token";

    private static readonly string[] SampleFeedback =
    {
        "The game crashes every time I open the Frozen Keep on my Pixel 8 running Android 14.",
        "Please add a co-op mode for the Dragon Quest storyline, it would be amazing with friends.",
        "Who is the Shadow King and what happens after the ending of the main campaign?",
        "This update is absolute garbage, the developers are idiots and ruined my favorite game.",
        "Love the new Ice Sword weapon, combat feels so much better now. Great work!",
        "App freezes on the loading screen after the 12.3.1 update. Lost all my progress and gems.",
        "The matchmaking disconnects constantly in ranked, huge lag spikes every match.",
        "Would be nice to have a dark theme and to remap the ability buttons.",
    };

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (db.Games.Any()) return;

        var packageId = "com.dreamgames.royalmatch";
        var game = new Game
        {
            Name = "Royal Match (Demo)",
            GooglePlayPackageId = packageId,
            GooglePlayUrl = $"https://play.google.com/store/apps/details?id={packageId}",
            SubmissionTokenHash = TokenGenerator.HashToken(DemoSubmissionToken),
            SubmissionEnabled = true
        };
        db.Games.Add(game);

        foreach (var text in SampleFeedback)
        {
            db.Feedback.Add(new Feedback
            {
                GameId = game.Id,
                Source = FeedbackSource.Internal,
                Text = text,
                ContentHash = ContentHasher.Hash(text),
                SourceCreatedAt = DateTime.UtcNow,
                ImportedAt = DateTime.UtcNow,
                Status = FeedbackStatus.Pending
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo game {GameId} with {Count} feedback items. " +
            "Public submit token: {Token}", game.Id, SampleFeedback.Length, DemoSubmissionToken);
    }
}
