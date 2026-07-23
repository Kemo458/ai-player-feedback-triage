using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PlayerFeedback.Api.Auth;

public class AuthOptions
{
    public string ManagerUsername { get; set; } = "manager";
    public string ManagerPassword { get; set; } = "manager-dev-pass";
    public string JwtSigningKey { get; set; } = "dev-signing-key-please-override-with-32-bytes-min";
    public string JwtIssuer { get; set; } = "player-feedback";
    public int TokenLifetimeHours { get; set; } = 12;
}

public class JwtTokenService
{
    private readonly AuthOptions _options;
    public JwtTokenService(IOptions<AuthOptions> options) => _options = options.Value;

    public (string token, DateTime expiresAt)? Login(string username, string password)
    {
        if (!string.Equals(username, _options.ManagerUsername, StringComparison.Ordinal) ||
            !string.Equals(password, _options.ManagerPassword, StringComparison.Ordinal))
            return null;

        var expires = DateTime.UtcNow.AddHours(_options.TokenLifetimeHours);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, "Manager"),
            new Claim(ClaimTypes.Role, "Reviewer"),
            new Claim(ClaimTypes.Role, "Administrator"),
        };
        var key = new SymmetricSecurityKey(KeyBytes(_options.JwtSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: _options.JwtIssuer, audience: _options.JwtIssuer,
            claims: claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public static byte[] KeyBytes(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        if (bytes.Length >= 32) return bytes;
        Array.Resize(ref bytes, 32); // pad short dev keys so HS256 never crashes
        return bytes;
    }
}
