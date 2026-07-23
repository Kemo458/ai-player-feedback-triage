using System.Security.Cryptography;
using System.Text;

namespace PlayerFeedback.Core.Common;

public static class ContentHasher
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    public static string Hash(params string?[] parts)
    {
        var joined = string.Join("", parts.Select(p => p ?? string.Empty));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public static class TokenGenerator
{
    public static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Return the canonical internal-feedback token for a game. The same game and
    /// signing secret always produce the same token, so its shareable link can be
    /// retrieved again without storing the raw token or rotating existing links.
    /// </summary>
    public static string StableForGame(Guid gameId, string secret)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var payload = Encoding.UTF8.GetBytes($"internal-feedback:{gameId:D}");
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(payload))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

public static class TextNormalizer
{
    /// <summary>Unicode normalize + whitespace collapse + case fold, for entity grouping.</summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(normalized.Length);
        bool lastWasSpace = false;
        foreach (var c in normalized.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Loose containment check used to verify entity evidence appears in the source text.</summary>
    public static bool Contains(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return false;
        var h = Normalize(haystack);
        var n = Normalize(needle);
        return h.Contains(n, StringComparison.Ordinal);
    }
}

public static class Cursor
{
    // Opaque cursor: base64(createdAtTicks:id)
    public static string Encode(DateTime createdAt, Guid id)
    {
        var raw = $"{createdAt.Ticks}:{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static bool TryDecode(string? cursor, out DateTime createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor)) return false;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4) { case 2: padded += "=="; break; case 3: padded += "="; break; }
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var idx = raw.IndexOf(':');
            if (idx <= 0) return false;
            createdAt = new DateTime(long.Parse(raw[..idx]), DateTimeKind.Utc);
            return Guid.TryParse(raw[(idx + 1)..], out id);
        }
        catch { return false; }
    }
}
