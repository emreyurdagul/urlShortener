using Microsoft.AspNetCore.Mvc;

namespace Monolith;

/// <summary>
/// The redirect hot path: GET /{code} → 302. A single-segment catch-all, so
/// literal routes (/api/*, /health, /metrics) always take precedence.
/// </summary>
[ApiController]
public sealed class RedirectController(LinkAppService links) : ControllerBase
{
    [HttpGet("/{code}")]
    public async Task<IActionResult> Follow(string code)
    {
        // No gateway in front: the request's own Host is the domain the code lives
        // under (X-Forwarded-Host only when a real proxy sets it).
        var domain = (Request.Headers["X-Forwarded-Host"].FirstOrDefault()
                      ?? Request.Host.Value ?? "").ToLowerInvariant();

        var click = new ClickEvent(
            domain, code,
            Request.Headers.Referer.FirstOrDefault(),
            Request.Headers.UserAgent.FirstOrDefault(),
            DateTime.UtcNow);

        var target = await links.ResolveAsync(domain, code, click);
        return target is null ? NotFound() : Redirect(target);
    }
}
