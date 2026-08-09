using System.Net.Sockets;
using Dapper;
using Npgsql;

namespace Monolith;

/// <summary>
/// Versioned, tracked schema migrations for the whole application (users, links,
/// clicks — one process owns them all). Each runs exactly once, recorded in
/// schema_migrations, under one advisory lock. Baselines use IF NOT EXISTS so
/// they record cleanly against an existing database.
/// </summary>
public static class Database
{
    private const long LockKey = 727274100;

    private static readonly (long Version, string Name, string Sql)[] Migrations =
    [
        (1, "users", """
            CREATE TABLE IF NOT EXISTS users (
                id            BIGSERIAL   PRIMARY KEY,
                email         TEXT        NOT NULL UNIQUE,
                password_hash TEXT        NOT NULL,
                plan          TEXT        NOT NULL DEFAULT 'free',
                created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """),
        (2, "links", """
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
        (3, "clicks", """
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
        (4, "refresh_tokens", """
            CREATE TABLE IF NOT EXISTS refresh_tokens (
                id         BIGSERIAL   PRIMARY KEY,
                user_id    BIGINT      NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                token_hash TEXT        NOT NULL UNIQUE,
                expires_at TIMESTAMPTZ NOT NULL,
                revoked_at TIMESTAMPTZ NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user ON refresh_tokens (user_id);
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
