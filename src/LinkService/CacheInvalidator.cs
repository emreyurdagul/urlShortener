using Npgsql;

namespace LinkService;

/// <summary>
/// Cross-replica cache invalidation over Postgres LISTEN/NOTIFY. With the redirect
/// cache living in each replica's memory, an edit/delete handled by one replica
/// would leave the others serving a stale (or deleted) target until TTL. Every
/// replica LISTENs on a channel and evicts the key the moment any replica NOTIFYs
/// it — no extra infrastructure, just Postgres.
/// </summary>
public sealed class CacheInvalidator(NpgsqlDataSource db, LinkCache cache, ILogger<CacheInvalidator> logger)
    : BackgroundService
{
    public const string Channel = "link_invalidate";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = await db.OpenConnectionAsync(stoppingToken);
                conn.Notification += (_, e) =>
                {
                    // Payload is "domain\ncode".
                    var nl = e.Payload.IndexOf('\n');
                    if (nl > 0)
                        cache.Remove(e.Payload[..nl], e.Payload[(nl + 1)..]);
                };

                await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", conn))
                    await listen.ExecuteNonQueryAsync(stoppingToken);

                logger.LogInformation("Listening for cache invalidations on '{Channel}'.", Channel);
                while (!stoppingToken.IsCancellationRequested)
                    await conn.WaitAsync(stoppingToken); // fires Notification, then loops
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogWarning("Cache invalidator reconnecting: {Message}", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); } catch (OperationCanceledException) { }
            }
        }
    }

    /// <summary>Tells every replica to drop a cached entry.</summary>
    public static async Task NotifyAsync(NpgsqlConnection conn, string domain, string code)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_notify(@ch, @payload)", conn);
        cmd.Parameters.AddWithValue("ch", Channel);
        cmd.Parameters.AddWithValue("payload", $"{domain}\n{code}");
        await cmd.ExecuteNonQueryAsync();
    }
}
