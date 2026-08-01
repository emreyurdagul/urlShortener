namespace Monolith;

/// <summary>
/// Application layer for click stats. The old analytics-service's read endpoint
/// becomes a method; writes flow in via <see cref="ClickWriter"/>, not HTTP.
/// </summary>
public sealed class AnalyticsAppService(ClickRepository clicks)
{
    public Task<(long Total, DateTime? LastClick)> StatsAsync(string code, string? domain) =>
        clicks.StatsAsync(code, domain);
}
