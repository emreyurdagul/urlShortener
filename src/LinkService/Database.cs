using System.Net.Sockets;
using Dapper;
using Npgsql;

namespace LinkService;

/// <summary>
/// Versioned, tracked schema migrations. Each migration runs exactly once
/// (recorded in schema_migrations); a Postgres advisory lock serializes the
/// concurrent replicas racing the same DDL. The baseline (v1) uses IF NOT EXISTS
/// so it is safe to record against a database whose tables already exist.
/// </summary>
public static class Database
{
    private const long LockKey = 727274001;

    private static readonly (long Version, string Name, string Sql)[] Migrations =
    [
        (1, "links", """
            CREATE TABLE IF NOT EXISTS links (
                id          BIGSERIAL   PRIMARY KEY,
                domain      TEXT        NOT NULL,
                code        TEXT        NOT NULL,
                target_url  TEXT        NOT NULL,
                owner_id    BIGINT      NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                UNIQUE (domain, code)
            );
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
