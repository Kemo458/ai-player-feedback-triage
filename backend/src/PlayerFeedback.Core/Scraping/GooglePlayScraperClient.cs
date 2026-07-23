using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PlayerFeedback.Core.Scraping;

public class ScraperOptions
{
    public string ScraperBaseUrl { get; set; } = "http://google-play-scraper:8080";
    public string InternalServiceKey { get; set; } = string.Empty;
    public int MaxReviewsPerImport { get; set; } = 500;
}

public record ScrapeRequest(string PackageId, int Count, string Language, string Country, string Sort, int? Score);

public record ScrapedReview(
    string ExternalId,
    string Text,
    string? Author,
    int? Rating,
    int ThumbsUpCount,
    string? AppVersion,
    DateTime? CreatedAt,
    string? DeveloperReply,
    DateTime? DeveloperRepliedAt);

public record ScrapeResult(string PackageId, int RequestedCount, int ReturnedCount, IReadOnlyList<ScrapedReview> Reviews);
public record GooglePlayAppMetadata(string PackageId, string? Title, string? IconUrl);

public class ScraperException : Exception
{
    public ScraperException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface IGooglePlayScraper
{
    Task<ScrapeResult> FetchReviewsAsync(ScrapeRequest request, CancellationToken ct);
    Task<GooglePlayAppMetadata> FetchAppMetadataAsync(string packageId, CancellationToken ct);
}

public class GooglePlayScraperClient : IGooglePlayScraper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly ScraperOptions _options;
    private readonly ILogger<GooglePlayScraperClient> _logger;

    public GooglePlayScraperClient(HttpClient http, IOptions<ScraperOptions> options, ILogger<GooglePlayScraperClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ScrapeResult> FetchReviewsAsync(ScrapeRequest request, CancellationToken ct)
    {
        var payload = new
        {
            packageId = request.PackageId,
            count = Math.Min(request.Count, _options.MaxReviewsPerImport),
            language = request.Language,
            country = request.Country,
            sort = request.Sort,
            score = request.Score
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.ScraperBaseUrl.TrimEnd('/')}/internal/v1/google-play/reviews");
        req.Headers.Add("X-Internal-Service-Key", _options.InternalServiceKey);
        req.Content = JsonContent.Create(payload);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ScraperException("Scraper request failed.", ex);
        }

        if (!resp.IsSuccessStatusCode)
            throw new ScraperException($"Scraper returned HTTP {(int)resp.StatusCode}.");

        var dto = await resp.Content.ReadFromJsonAsync<ScraperResponseDto>(Json, ct)
            ?? throw new ScraperException("Scraper returned empty body.");

        var reviews = (dto.Reviews ?? new List<ScraperReviewDto>())
            .Where(r => !string.IsNullOrWhiteSpace(r.ExternalId) && !string.IsNullOrWhiteSpace(r.Text))
            .Select(r => new ScrapedReview(
                r.ExternalId!, r.Text!, r.Author, r.Rating, r.ThumbsUpCount,
                r.AppVersion, ToUtc(r.CreatedAt), r.DeveloperReply, ToUtc(r.DeveloperRepliedAt)))
            .ToList();

        _logger.LogInformation("Scraper returned {Returned} reviews for {Package}", reviews.Count, request.PackageId);
        return new ScrapeResult(dto.PackageId ?? request.PackageId, dto.RequestedCount, reviews.Count, reviews);
    }

    public async Task<GooglePlayAppMetadata> FetchAppMetadataAsync(string packageId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{_options.ScraperBaseUrl.TrimEnd('/')}/internal/v1/google-play/apps/{Uri.EscapeDataString(packageId)}");
        req.Headers.Add("X-Internal-Service-Key", _options.InternalServiceKey);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ScraperException("App artwork request failed.", ex);
        }

        if (!resp.IsSuccessStatusCode)
            throw new ScraperException($"App artwork service returned HTTP {(int)resp.StatusCode}.");

        var dto = await resp.Content.ReadFromJsonAsync<AppMetadataResponseDto>(Json, ct)
            ?? throw new ScraperException("App artwork service returned an empty body.");
        return new GooglePlayAppMetadata(
            dto.PackageId ?? packageId,
            dto.Title,
            dto.IconUrl);
    }

    // Postgres timestamptz columns require Kind=Utc. JSON dates may arrive as Local/Unspecified.
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } d => d,
        { Kind: DateTimeKind.Local } d => d.ToUniversalTime(),
        { } d => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };

    private class ScraperResponseDto
    {
        public string? PackageId { get; set; }
        public int RequestedCount { get; set; }
        public int ReturnedCount { get; set; }
        public List<ScraperReviewDto>? Reviews { get; set; }
    }

    private class ScraperReviewDto
    {
        public string? ExternalId { get; set; }
        public string? Text { get; set; }
        public string? Author { get; set; }
        public int? Rating { get; set; }
        public int ThumbsUpCount { get; set; }
        public string? AppVersion { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? DeveloperReply { get; set; }
        public DateTime? DeveloperRepliedAt { get; set; }
    }

    private class AppMetadataResponseDto
    {
        public string? PackageId { get; set; }
        public string? Title { get; set; }
        public string? IconUrl { get; set; }
    }
}
