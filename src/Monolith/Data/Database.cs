using System.Net.Sockets;
using Dapper;
using Npgsql;

namespace Monolith;

/// <summary>
/// One migration for the whole application. In the microservice build each
/// service owned its own schema and ran its own migration under a distinct
/// advisory lock (database-per-service). Here a single process owns every table,
/// so one migration under one lock creates them all.
/// </summary>
public static class Database
{
    private const string Schema = """
        SELECT pg_advisory_lock(727274100);

        CREATE TABLE IF NOT EXISTS users (
            id            BIGSERIAL   PRIMARY KEY,
            email         TEXT        NOT NULL UNIQUE,
            password_hash TEXT        NOT NULL,
            plan          TEXT        NOT NULL DEFAULT 'free',
            created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS links (
            id          BIGSERIAL   PRIMARY KEY,
            domain      TEXT        NOT NULL,
            code        TEXT        NOT NULL,
            target_url  TEXT        NOT NULL,
            owner_id    BIGINT      NULL,
            created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
            UNIQUE (domain, code)
        );

        CREATE TABLE IF NOT EXISTS clicks (
            id         BIGSERIAL   PRIMARY KEY,
            domain     TEXT        NOT NULL,
            code       TEXT        NOT NULL,
            referer    TEXT        NULL,
            user_agent TEXT        NULL,
            clicked_at TIMESTAMPTZ NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_clicks_code ON clicks (domain, code);

        SELECT pg_advisory_unlock(727274100);
        """;

    public static async Task MigrateAsync(NpgsqlDataSource db, ILogger logger, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                await conn.ExecuteAsync(Schema);
                logger.LogInformation("Database schema is ready.");
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
