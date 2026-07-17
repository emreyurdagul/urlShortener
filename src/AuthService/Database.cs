using System.Net.Sockets;
using Dapper;
using Npgsql;

namespace AuthService;

public static class Database
{
    private const string Schema = """
        SELECT pg_advisory_lock(727274002);
        CREATE TABLE IF NOT EXISTS users (
            id            BIGSERIAL   PRIMARY KEY,
            email         TEXT        NOT NULL UNIQUE,
            password_hash TEXT        NOT NULL,
            plan          TEXT        NOT NULL DEFAULT 'free',
            created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
        );
        SELECT pg_advisory_unlock(727274002);
        """;

    public static async Task MigrateAsync(NpgsqlDataSource db, ILogger logger, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                await conn.ExecuteAsync(Schema);
                logger.LogInformation("Auth schema is ready.");
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
