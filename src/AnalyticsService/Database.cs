using System.Net.Sockets;
using Dapper;
using Npgsql;

namespace AnalyticsService;

/// <summary>Versioned, tracked schema migrations (see LinkService.Database for the shape).</summary>
public static class Database
{
    private const long LockKey = 727274003;

    private static readonly (long Version, string Name, string Sql)[] Migrations =
    [
        (1, "clicks", """
            CREATE TABLE IF NOT EXISTS clicks (
                id         BIGSERIAL   PRIMARY KEY,
                domain     TEXT        NOT NULL,
                code       TEXT        NOT NULL,
                referer    TEXT        NULL,
                user_agent TEXT        NULL,
                clicked_at TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_clicks_code ON clicks (domain, code);
            """),
    ];

    public static async Task MigrateAsync(NpgsqlDataSource db, ILogger logger, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                await conn.ExecuteAsync($"SELECT pg_advisory_lock({LockKey})");
                try
                {
                    await conn.ExecuteAsync(
                        "CREATE TABLE IF NOT EXISTS schema_migrations (version BIGINT PRIMARY KEY, name TEXT NOT NULL, applied_at TIMESTAMPTZ NOT NULL DEFAULT now())");
                    var applied = (await conn.QueryAsync<long>("SELECT version FROM schema_migrations")).ToHashSet();
                    foreach (var (version, name, sql) in Migrations)
                    {
                        if (applied.Contains(version))
                            continue;
                        await conn.ExecuteAsync(sql);
                        await conn.ExecuteAsync(
                            "INSERT INTO schema_migrations (version, name) VALUES (@version, @name)", new { version, name });
                        logger.LogInformation("Applied migration {Version} ({Name}).", version, name);
                    }
                    logger.LogInformation("Schema up to date ({Count} migration(s)).", Migrations.Length);
                }
                finally
                {
                    await conn.ExecuteAsync($"SELECT pg_advisory_unlock({LockKey})");
                }
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
