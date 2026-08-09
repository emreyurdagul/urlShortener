using Microsoft.AspNetCore.Mvc;

namespace Monolith;

/// <summary>
/// Presentation layer for auth. Thin: validates input shape, delegates to
/// <see cref="AuthAppService"/>, maps the result to an HTTP status.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthAppService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(Credentials req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { code = "email_invalid", error = "A valid email is required." });
        if (req.Password is not { Length: >= 8 })
            return BadRequest(new { code = "password_short", error = "Password must be at least 8 characters.", min = 8 });

        var result = await auth.RegisterAsync(req.Email, req.Password);
        return result is null
            ? Conflict(new { code = "email_taken", error = "Email already registered." })
            : Ok(new { token = result.Token, expiresAt = result.ExpiresAt, plan = result.Plan, refreshToken = result.RefreshToken });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(Credentials req)
    {
        var result = await auth.LoginAsync(req.Email, req.Password);
        return result is null
            ? Unauthorized(new { code = "bad_credentials", error = "Wrong email or password." })
            : Ok(new { token = result.Token, expiresAt = result.ExpiresAt, plan = result.Plan, refreshToken = result.RefreshToken });
    }

    // Exchange a refresh token for a fresh access token (rotates the refresh token).
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest(new { code = "refresh_invalid", error = "Missing refresh token." });

        var result = await auth.RefreshAsync(req.RefreshToken);
        return result is null
            ? Unauthorized(new { code = "refresh_invalid", error = "Invalid or expired refresh token." })
            : Ok(new { token = result.Token, expiresAt = result.ExpiresAt, plan = result.Plan, refreshToken = result.RefreshToken });
    }

    // Revoke a refresh token (logout). Idempotent.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.RefreshToken))
            await auth.LogoutAsync(req.RefreshToken);
        return NoContent();
    }

    // Self-serve tier upgrade for the AUTHENTICATED caller (identity comes from
    // AuthMiddleware, not a client-supplied user id — so a user can only upgrade
    // themselves). Payment is simulated; the DB + fresh-token flow is real.
    [HttpPost("upgrade")]
    public async Task<IActionResult> Upgrade(UpgradeRequest req)
    {
        if (HttpContext.Items[AuthMiddleware.ItemKey] is not UserIdentity user)
            return Unauthorized(new { code = "unauthenticated", error = "Sign in required." });

        var plan = Plans.Normalize(req.Plan);
        if (!Plans.IsValid(plan) || plan == Plans.Free)
            return BadRequest(new { code = "plan_invalid", error = "Choose a paid tier: 'plus' or 'pro'." });

        var result = await auth.UpgradeAsync(user.UserId, plan);
        return result is null
            ? NotFound(new { code = "user_not_found", error = "Account not found." })
            : Ok(new { token = result.Token, expiresAt = result.ExpiresAt, plan = result.Plan });
    }
}
