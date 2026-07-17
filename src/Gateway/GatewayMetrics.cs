using Prometheus;

namespace Gateway;

/// <summary>
/// RED metrics for the proxy: rate + errors via the request counter's status
/// label, duration via a histogram sized for millisecond-level redirects.
/// </summary>
public static class GatewayMetrics
{
    public static readonly Counter Requests = Metrics.CreateCounter(
        "gateway_requests_total",
        "Requests proxied by the gateway, labeled by pool, backend, method and status code.",
        new CounterConfiguration { LabelNames = ["pool", "backend", "method", "code"] });

    public static readonly Histogram RequestDuration = Metrics.CreateHistogram(
        "gateway_request_duration_seconds",
        "Time from picking a backend to fully relaying its response.",
        new HistogramConfiguration
        {
            LabelNames = ["pool", "backend"],
            Buckets = [0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10],
        });

    public static readonly Counter Failovers = Metrics.CreateCounter(
        "gateway_failovers_total",
        "Requests retried on another backend after a connection-level failure.",
        new CounterConfiguration { LabelNames = ["pool", "backend"] });

    public static readonly Gauge BackendHealthy = Metrics.CreateGauge(
        "gateway_backend_healthy",
        "1 while the backend is in rotation, 0 while ejected.",
        new GaugeConfiguration { LabelNames = ["pool", "backend"] });
}
