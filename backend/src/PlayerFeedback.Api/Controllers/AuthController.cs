using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlayerFeedback.Api.Auth;
using PlayerFeedback.Core.Contracts;

namespace PlayerFeedback.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly JwtTokenService _tokens;
    public AuthController(JwtTokenService tokens) => _tokens = tokens;

    [AllowAnonymous]
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var result = _tokens.Login(request.Username ?? "", request.Password ?? "");
        if (result is null)
            return Problem(statusCode: 401, title: "Invalid credentials.");
        return Ok(new LoginResponse(result.Value.token, result.Value.expiresAt));
    }
}
