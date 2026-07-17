using System.Net.Http.Json;
using Prometheus;

namespace LinkService;

/// <summary>
/// Drains the click channel and posts batches to analytics-service. Batches
/// by size or a short time window so a trickle of clicks still gets flushed.
/// </summary>
public sealed class ClickShipper(ClickRecorder recorder, IHttpClientFactory httpFactory, IConfiguration config, ILogger<ClickShipper> logger)
    : BackgroundService
{
    private const int MaxBatch = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private static readonly Counter Dropped = Metrics.CreateCounter(
        "clicks_shipping_failed_total", "Click batches that could not be delivered to analytics.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = (config["ANALYTICS_URL"] ?? "http://analytics-service:8080").TrimEnd('/') + "/api/clicks";
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
                // Flush window elapsed — ship what we have.
            }

            if (batch.Count > 0)
                await ShipAsync(endpoint, batch, stoppingToken);
        }
    }

    private static async Task<bool> WaitForNext(System.Threading.Channels.ChannelReader<ClickEvent> reader, CancellationToken ct)
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

    private async Task ShipAsync(string endpoint, List<ClickEvent> batch, CancellationToken ct)
    {
        try
        {
            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            using var response = await client.PostAsJsonAsync(endpoint, batch, ct);
            if (!response.IsSuccessStatusCode)
            {
                Dropped.Inc();
                logger.LogWarning("Analytics rejected a batch of {Count}: {Status}", batch.Count, response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            Dropped.Inc();
            logger.LogWarning("Could not ship {Count} clicks to analytics: {Message}", batch.Count, ex.Message);
        }
    }
}
