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
            return BadRequest(new { error = "A valid email is required." });
        if (req.Password is not { Length: >= 8 })
            return BadRequest(new { error = "Password must be at least 8 characters." });

        var result = await auth.RegisterAsync(req.Email, req.Password);
        return result is null
            ? Conflict(new { error = "Email already registered." })
            : Ok(new { token = result.Token, expiresAt = result.ExpiresAt, plan = result.Plan });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(Credentials req)
    {
        var result = await auth.LoginAsync(req.Email, req.Password);
        return result is null
            ? Unauthorized()
            : Ok(new { token = result.Token, expiresAt = result.ExpiresAt, plan = result.Plan });
    }

    // Self-serve tier upgrade for the AUTHENTICATED caller (identity comes from
    // AuthMiddleware, not a client-supplied user id — so a user can only upgrade
    // themselves). Payment is simulated; the DB + fresh-token flow is real.
    [HttpPost("upgrade")]
    public async Task<IActionResult> Upgrade(UpgradeRequest req)
    {
        if (HttpContext.Items[AuthMiddleware.ItemKey] is not UserIdentity user)
            return Unauthorized();

        var plan = Plans.Normalize(req.Plan);
        if (!Plans.IsValid(plan) || plan == Plans.Free)
            return BadRequest(new { error = "Choose a paid tier: 'plus' or 'pro'." });

        var result = await auth.UpgradeAsync(user.UserId, plan);
        return result is null
            ? NotFound()
            : Ok(new { token = result.Token, expiresAt = result.ExpiresAt, plan = result.Plan });
    }
}
