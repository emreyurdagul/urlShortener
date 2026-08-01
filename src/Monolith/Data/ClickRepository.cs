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
