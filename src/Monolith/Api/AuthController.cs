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

    // Phase-6 payment flow will drive this; for now it lets the tier machinery
    // be exercised end-to-end.
    [HttpPost("plan")]
    public async Task<IActionResult> SetPlan(SetPlanRequest req)
    {
        if (!Plans.IsValid(req.Plan))
            return BadRequest(new { error = "Unknown plan." });

        return await auth.SetPlanAsync(req.UserId, req.Plan)
            ? Ok(new { req.UserId, req.Plan })
            : NotFound();
    }
}
