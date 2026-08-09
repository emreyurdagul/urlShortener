namespace Monolith;

/// <summary>
/// Application layer for click stats. The old analytics-service's read endpoint
/// becomes a method; writes flow in via <see cref="ClickWriter"/>, not HTTP.
/// </summary>
public sealed class AnalyticsAppService(ClickRepository clicks)
{
    public sealed record Detail(
        long Total,
        DateTime? LastClick,
        IReadOnlyList<(string Day, long Count)> Daily,
        IReadOnlyList<(string Referer, long Count)> TopReferrers);

    public async Task<Detail> DetailAsync(string code, string? domain)
    {
        var (total, last) = await clicks.StatsAsync(code, domain);
        var daily = await clicks.DailyAsync(code, domain);
        var referrers = await clicks.TopReferrersAsync(code, domain);
        return new Detail(total, last, daily, referrers);
    }
}
