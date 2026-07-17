using System.Net.Sockets;
using Dapper;
using Npgsql;

namespace LinkService;

public static class Database
{
    // The advisory lock serializes concurrent replicas racing the same DDL.
    private const string Schema = """
        SELECT pg_advisory_lock(727274001);
        CREATE TABLE IF NOT EXISTS links (
            id          BIGSERIAL   PRIMARY KEY,
            domain      TEXT        NOT NULL,
            code        TEXT        NOT NULL,
            target_url  TEXT        NOT NULL,
            owner_id    BIGINT      NULL,
            created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
            UNIQUE (domain, code)
        );
        SELECT pg_advisory_unlock(727274001);
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
