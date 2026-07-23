using System.Text.RegularExpressions;
using System.Web;

namespace PlayerFeedback.Core.Scraping;

public static partial class PlayStoreUrl
{
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$")]
    private static partial Regex PackageIdRegex();

    /// <summary>
    /// Validates a Google Play app URL and extracts the package id.
    /// Only host play.google.com and path /store/apps/details with a valid id param are accepted.
    /// </summary>
    public static bool TryParsePackageId(string? url, out string packageId, out string? error)
    {
        packageId = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL is required.";
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            error = "URL is not a valid absolute URL.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "URL must use HTTPS.";
            return false;
        }

        if (!uri.Host.Equals("play.google.com", StringComparison.OrdinalIgnoreCase))
        {
            error = "Host must be exactly play.google.com.";
            return false;
        }

        if (!uri.AbsolutePath.Equals("/store/apps/details", StringComparison.OrdinalIgnoreCase))
        {
            error = "Path must be /store/apps/details.";
            return false;
        }

        var id = HttpUtility.ParseQueryString(uri.Query).Get("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            error = "Missing 'id' query parameter.";
            return false;
        }

        id = id.Trim();
        if (id.Length > 200 || !PackageIdRegex().IsMatch(id))
        {
            error = "Invalid package id.";
            return false;
        }

        packageId = id;
        return true;
    }

    public static string Canonical(string packageId) =>
        $"https://play.google.com/store/apps/details?id={packageId}";
}
