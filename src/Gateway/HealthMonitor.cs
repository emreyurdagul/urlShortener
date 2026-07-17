namespace Gateway;

/// <summary>
/// Actively probes every backend on a fixed interval and flips its health
/// state. Recovery also happens here: a backend passively ejected by the
/// proxy is put back in rotation as soon as a probe succeeds.
/// </summary>
public sealed class HealthMonitor(RouteTable routes, GatewayConfig config, ILogger<HealthMonitor> logger)
    : BackgroundService
{
    private static readonly HttpMessageInvoker Client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(2),
    });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeout = TimeSpan.FromSeconds(config.HealthCheck.TimeoutSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(config.HealthCheck.IntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var probes = routes.Pools
                .SelectMany(pool => pool.Backends.Select(backend => (pool, backend)))
                .Select(x => ProbeAsync(x.pool, x.backend, timeout, stoppingToken));
            await Task.WhenAll(probes);
        }
    }

    private async Task ProbeAsync(IBackendPool pool, Backend backend, TimeSpan timeout, CancellationToken ct)
    {
        bool healthy;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(new Uri(backend.Url), config.HealthCheck.Path));
            using var response = await Client.SendAsync(request, cts.Token);
            healthy = response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
                return;
            healthy = false;
        }

        GatewayMetrics.BackendHealthy.WithLabels(pool.Name, backend.Url).Set(healthy ? 1 : 0);

        if (backend.SetHealthy(healthy))
        {
            logger.Log(healthy ? LogLevel.Information : LogLevel.Warning,
                "Backend {Backend} in pool '{Pool}' is {State}",
                backend.Url, pool.Name, healthy ? "UP" : "DOWN");
        }
    }
}
