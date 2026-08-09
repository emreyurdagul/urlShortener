using Dapper;
using Npgsql;

namespace Monolith;

/// <summary>Data-access layer for the clicks table (the old analytics-service's DB work).</summary>
public sealed class ClickRepository(NpgsqlDataSource db)
{
    /// <summary>Dapper multi-execs the INSERT once per event in the batch.</summary>
    public async Task InsertBatchAsync(IReadOnlyList<ClickEvent> events)
    {
        if (events.Count == 0)
            return;

        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO clicks (domain, code, referer, user_agent, clicked_at)
            VALUES (@Domain, @Code, @Referer, @UserAgent, @ClickedAt)
            """,
            events);
    }

    public async Task<IReadOnlyList<(string Day, long Count)>> DailyAsync(string code, string? domain)
    {
        await using var conn = await db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<(string, long)>(
            """
            SELECT to_char(date_trunc('day', clicked_at), 'YYYY-MM-DD'), COUNT(*)
            FROM clicks
            WHERE code = @code AND (@domain IS NULL OR domain = @domain)
              AND clicked_at >= now() - interval '7 days'
            GROUP BY 1 ORDER BY 1
            """, new { code, domain });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<(string Referer, long Count)>> TopReferrersAsync(string code, string? domain)
    {
        await using var conn = await db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<(string, long)>(
            """
            SELECT COALESCE(NULLIF(referer, ''), 'direct'), COUNT(*)
            FROM clicks
            WHERE code = @code AND (@domain IS NULL OR domain = @domain)
            GROUP BY 1 ORDER BY 2 DESC LIMIT 5
            """, new { code, domain });
        return rows.ToList();
    }

    public async Task<(long Total, DateTime? LastClick)> StatsAsync(string code, string? domain)
    {
        await using var conn = await db.OpenConnectionAsync();
        var row = await conn.QueryFirstOrDefaultAsync(
            """
            SELECT COUNT(*) AS total, MAX(clicked_at) AS last_click
            FROM clicks
            WHERE code = @code AND (@domain IS NULL OR domain = @domain)
            """,
            new { code, domain });

        long total = row?.total ?? 0L;
        DateTime? lastClick = row?.last_click;
        return (total, lastClick);
    }
}
