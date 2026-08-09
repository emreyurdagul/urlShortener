using Microsoft.AspNetCore.Mvc;

namespace Monolith;

/// <summary>Presentation layer for per-code click stats.</summary>
[ApiController]
public sealed class AnalyticsController(AnalyticsAppService analytics) : ControllerBase
{
    [HttpGet("/api/analytics/{code}")]
    public async Task<IActionResult> Stats(string code, [FromQuery] string? domain)
    {
        var s = await analytics.DetailAsync(code, domain);
        return Ok(new
        {
            code,
            total = s.Total,
            lastClick = s.LastClick,
            daily = s.Daily.Select(d => new { day = d.Day, count = d.Count }),
            topReferrers = s.TopReferrers.Select(r => new { referer = r.Referer, count = r.Count }),
        });
    }
}
