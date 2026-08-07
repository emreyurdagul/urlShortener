using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Gateway;

/// <summary>
/// Streams a live RED snapshot to a dashboard WebSocket. Each snapshot is
/// assembled from Prometheus instant queries, so the custom dashboard shows
/// the same numbers Grafana does — just pushed in real time.
/// </summary>
public sealed class MetricsFeed(IHttpClientFactory httpFactory, IConfiguration config, ILogger<MetricsFeed> logger)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _promBase =
        (config["PROMETHEUS_URL"] ?? "http://prometheus:9090").TrimEnd('/');

    public async Task StreamAsync(WebSocket socket, CancellationToken ct)
    {
        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(4);

        // Detect client-side close without blocking the send loop.
        var closed = ReceiveUntilCloseAsync(socket, ct);

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested && !closed.IsCompleted)
            {
                var snapshot = await BuildSnapshotAsync(client, ct);
                var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, Json);
                await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);

                await Task.WhenAny(Task.Delay(Interval, ct), closed);
            }
        }
        catch (OperationCanceledException) { /* shutting down or client gone */ }
        catch (WebSocketException ex)
        {
            logger.LogDebug("Metrics socket closed: {Message}", ex.Message);
        }
        finally
        {
            if (socket.State == WebSocketState.Open)
                await TryCloseAsync(socket);
        }
    }

    private async Task<object> BuildSnapshotAsync(HttpClient client, CancellationToken ct)
    {
        var requestRate = await ScalarAsync(client, "sum(rate(gateway_requests_total[1m]))", ct);
        var errorRate = await ScalarAsync(client, "sum(rate(gateway_requests_total{code=~\"5..\"}[1m]))", ct);
        var rateLimited = await ScalarAsync(client, "rate(gateway_rate_limited_total[1m])", ct);
        // Cumulative counter — a real, non-zero number even when the live rate is
        // zero, so an idle dashboard still shows something true rather than reading
        // as dead.
        var totalRequests = await ScalarAsync(client, "sum(gateway_requests_total)", ct);
        var backends = await BackendHealthAsync(client, ct);

        // Latency percentiles are only meaningful with recent traffic. Over an
        // empty 1m window histogram_quantile returns bucket-boundary artifacts
        // (e.g. a frozen "p95 = 242ms" while nothing is happening) — which reads as
        // fake. Report null when idle and let the UI show "—".
        double? p50Ms = null, p95Ms = null, p99Ms = null;
        if (requestRate > 0)
        {
            p50Ms = await ScalarAsync(client, Quantile(0.50), ct) * 1000;
            p95Ms = await ScalarAsync(client, Quantile(0.95), ct) * 1000;
            p99Ms = await ScalarAsync(client, Quantile(0.99), ct) * 1000;
        }

        return new
        {
            ts = DateTimeOffset.UtcNow,
            requestRate,
            totalRequests,
            errorRatio = requestRate > 0 ? errorRate / requestRate : 0,
            rateLimitedRate = rateLimited,
            p50Ms,
            p95Ms,
            p99Ms,
            backends,
        };
    }

    private static string Quantile(double q) =>
        $"histogram_quantile({q}, sum by (le) (rate(gateway_request_duration_seconds_bucket[1m])))";

    private async Task<double> ScalarAsync(HttpClient client, string query, CancellationToken ct)
    {
        try
        {
            var url = $"{_promBase}/api/v1/query?query={Uri.EscapeDataString(query)}";
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            var result = doc.RootElement.GetProperty("data").GetProperty("result");
            if (result.GetArrayLength() == 0)
                return 0;

            var raw = result[0].GetProperty("value")[1].GetString();
            return double.TryParse(raw, out var value) && !double.IsNaN(value) ? value : 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return 0;
        }
    }

    private async Task<object[]> BackendHealthAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            // max by (backend) collapses stale series left behind by earlier
            // container instances so each backend appears once.
            var url = $"{_promBase}/api/v1/query?query={Uri.EscapeDataString("max by (backend) (gateway_backend_healthy)")}";
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            var result = doc.RootElement.GetProperty("data").GetProperty("result");

            var list = new List<object>();
            foreach (var series in result.EnumerateArray())
            {
                var backend = series.GetProperty("metric").GetProperty("backend").GetString() ?? "?";
                var healthy = series.GetProperty("value")[1].GetString() == "1";
                list.Add(new { backend, healthy });
            }
            return [.. list];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return [];
        }
    }

    private static async Task ReceiveUntilCloseAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[256];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // Treated as a close.
        }
    }

    private static async Task TryCloseAsync(WebSocket socket)
    {
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (WebSocketException) { /* already gone */ }
    }
}
