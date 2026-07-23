using System.Text;

namespace PlayerFeedback.Api.Controllers;

/// <summary>Opaque offset-based cursor. The client treats it as opaque; we encode an offset.</summary>
public static class OffsetCursor
{
    public static string Encode(int offset)
    {
        var raw = $"o:{offset}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static int Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4) { case 2: padded += "=="; break; case 3: padded += "="; break; }
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return raw.StartsWith("o:") && int.TryParse(raw[2..], out var o) && o >= 0 ? o : 0;
        }
        catch { return 0; }
    }
}
