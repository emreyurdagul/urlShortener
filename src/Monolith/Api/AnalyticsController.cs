using Microsoft.AspNetCore.Mvc;

namespace Monolith;

/// <summary>Presentation layer for per-code click stats.</summary>
[ApiController]
public sealed class AnalyticsController(AnalyticsAppService analytics) : ControllerBase
{
    [HttpGet("/api/analytics/{code}")]
    public async Task<IActionResult> Stats(string code, [FromQuery] string? domain)
    {
        var (total, lastClick) = await analytics.StatsAsync(code, domain);
        return Ok(new { code, total, lastClick });
    }
}
