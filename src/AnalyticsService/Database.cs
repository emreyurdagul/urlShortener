using System.Net.Sockets;
using Dapper;
using Npgsql;

namespace AnalyticsService;

public static class Database
{
    private const string Schema = """
        SELECT pg_advisory_lock(727274003);
        CREATE TABLE IF NOT EXISTS clicks (
            id         BIGSERIAL   PRIMARY KEY,
            domain     TEXT        NOT NULL,
            code       TEXT        NOT NULL,
            referer    TEXT        NULL,
            user_agent TEXT        NULL,
            clicked_at TIMESTAMPTZ NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_clicks_code ON clicks (domain, code);
        SELECT pg_advisory_unlock(727274003);
        """;

    public static async Task MigrateAsync(NpgsqlDataSource db, ILogger logger, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                await conn.ExecuteAsync(Schema);
                logger.LogInformation("Analytics schema is ready.");
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or SocketException && attempt < 15)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}

public sealed record ClickEvent(string Domain, string Code, string? Referer, string? UserAgent, DateTime ClickedAt);
