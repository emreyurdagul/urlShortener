using System.Net.Sockets;
using Dapper;
using Npgsql;

namespace AuthService;

/// <summary>
/// Versioned, tracked schema migrations (see LinkService.Database for the shape).
/// The baseline (v1) uses IF NOT EXISTS so it records cleanly against an existing
/// database; later migrations run exactly once.
/// </summary>
public static class Database
{
    private const long LockKey = 727274002;

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
        (2, "refresh_tokens", """
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

public sealed record UserRow(long Id, string Email, string PasswordHash, string Plan);
