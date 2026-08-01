using System.Threading.Channels;
using Prometheus;

namespace Monolith;

/// <summary>
/// Drains the click channel and persists batches straight to Postgres.
///
/// This is the monolith counterpart of the microservice ClickShipper. Same
/// batching (by size or a short time window so a trickle still flushes), but the
/// batch is written to the DB in-process instead of POSTed over HTTP to a
/// separate analytics service. The network hop — and every way it could fail —
/// simply disappears.
/// </summary>
public sealed class ClickWriter(ClickRecorder recorder, ClickRepository clicks, ILogger<ClickWriter> logger)
    : BackgroundService
{
    private const int MaxBatch = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private static readonly Counter Ingested = Metrics.CreateCounter(
        "app_clicks_ingested_total", "Click events persisted.");
    private static readonly Counter Failed = Metrics.CreateCounter(
        "app_clicks_write_failed_total", "Click batches that could not be persisted.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ClickEvent>(MaxBatch);
        var reader = recorder.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            batch.Clear();
            try
            {
                // Block until at least one event, then greedily drain up to a
                // full batch before flushing.
                var first = await reader.ReadAsync(stoppingToken);
                batch.Add(first);
                using var window = new CancellationTokenSource(FlushInterval);
                while (batch.Count < MaxBatch && await WaitForNext(reader, window.Token))
                {
                    while (batch.Count < MaxBatch && reader.TryRead(out var next))
                        batch.Add(next);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Flush window elapsed — persist what we have.
            }

            if (batch.Count > 0)
                await WriteAsync(batch);
        }
    }

    private static async Task<bool> WaitForNext(ChannelReader<ClickEvent> reader, CancellationToken ct)
    {
        try
        {
            return await reader.WaitToReadAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task WriteAsync(List<ClickEvent> batch)
    {
        try
        {
            await clicks.InsertBatchAsync(batch);
            Ingested.Inc(batch.Count);
        }
        catch (Exception ex)
        {
            Failed.Inc();
            logger.LogWarning("Could not persist {Count} clicks: {Message}", batch.Count, ex.Message);
        }
    }
}
