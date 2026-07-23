using Microsoft.EntityFrameworkCore;
using PlayerFeedback.Core.Domain;

namespace PlayerFeedback.Core.Data;

public record FeedbackFilter(
    string? Source = null, string? Tag = null, string? Severity = null,
    string? Sentiment = null, string? Status = null, string? Entity = null, string? Search = null);

public static class FeedbackQuery
{
    public static IQueryable<Feedback> Apply(IQueryable<Feedback> query, Guid gameId, FeedbackFilter f)
    {
        query = query.Where(x => x.GameId == gameId);

        if (Enum.TryParse<FeedbackSource>(f.Source, true, out var src) && !string.IsNullOrWhiteSpace(f.Source))
            query = query.Where(x => x.Source == src);

        if (string.Equals(f.Status, "Active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Status == FeedbackStatus.Pending
                || x.Status == FeedbackStatus.Processing
                || x.Status == FeedbackStatus.RetryScheduled);
        else if (Enum.TryParse<FeedbackStatus>(f.Status, true, out var st) && !string.IsNullOrWhiteSpace(f.Status))
            query = query.Where(x => x.Status == st);

        if (!string.IsNullOrWhiteSpace(f.Tag))
        {
            var tag = f.Tag.Trim();
            query = query.Where(x => x.Analysis != null && x.Analysis.Tags.Contains(tag));
        }

        if (Enum.TryParse<Severity>(f.Severity, true, out var sev) && !string.IsNullOrWhiteSpace(f.Severity))
            query = query.Where(x => x.Analysis != null && x.Analysis.Severity == sev);

        if (Enum.TryParse<Sentiment>(f.Sentiment, true, out var sen) && !string.IsNullOrWhiteSpace(f.Sentiment))
            query = query.Where(x => x.Analysis != null && x.Analysis.Sentiment == sen);

        if (!string.IsNullOrWhiteSpace(f.Entity))
        {
            var norm = f.Entity.Trim().ToLowerInvariant();
            query = query.Where(x => x.Analysis != null &&
                x.Analysis.Entities.Any(e => e.NormalizedName == norm));
        }

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = $"%{f.Search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Text, s) ||
                (x.Analysis != null && EF.Functions.ILike(x.Analysis.Summary, s)));
        }

        return query;
    }
}
