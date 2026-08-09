using Gateway;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var configPath = builder.Configuration["GATEWAY_CONFIG"] ?? "gateway.json";
var config = GatewayConfig.Load(configPath);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(new RouteTable(config));
builder.Services.AddSingleton<ProxyHandler>();
builder.Services.AddSingleton<JwtValidator>();
builder.Services.AddSingleton<MetricsFeed>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<HealthMonitor>();

var rateCapacity = builder.Configuration.GetValue("RATE_LIMIT_BURST", 60);
var rateRefill = builder.Configuration.GetValue("RATE_LIMIT_PER_SECOND", 30.0);
builder.Services.AddSingleton(new TokenBucketRateLimiter(rateCapacity, rateRefill));

// Distributed tracing: a server span per proxied request + a client span per
// backend hop (Gateway.Proxy), exported over OTLP to Tempo. Only proxied traffic
// is traced — /metrics, /ws/metrics and /health are excluded.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("gateway"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
        {
            // Path-based: the endpoint isn't resolved yet when the filter runs, so
            // GetEndpoint() would be null for everything. Exclude the gateway's own
            // infra routes (/metrics, /ws/metrics, /health) from traces.
            var path = ctx.Request.Path;
            return !path.StartsWithSegments("/metrics")
                && !path.StartsWithSegments("/ws")
                && !path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation()
        .AddSource("Gateway.Proxy"))
    .UseOtlpExporter();

var app = builder.Build();

app.Logger.LogInformation("Gateway routes: {Routes}",
    string.Join(", ", config.Routes.Select(r =>
        $"{r.PathPrefix} -> {r.Pool} [{config.Pools[r.Pool].Count} backend(s)]")));

app.UseWebSockets();
app.UseRouting();
app.MapMetrics();

// Live dashboard feed. A gateway-owned terminal branch, so it bypasses the
// proxy (and the auth/rate-limit chain) just like /metrics does.
app.Map("/ws/metrics", branch => branch.Run(async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var feed = context.RequestServices.GetRequiredService<MetricsFeed>();
    await feed.StreamAsync(socket, context.RequestAborted);
}));

// Proxied traffic passes auth then rate limiting before reaching a backend;
// the gateway's own endpoints (/metrics, /ws/metrics) bypass both.
var proxy = app.Services.GetRequiredService<ProxyHandler>();
app.UseWhen(context => context.GetEndpoint() is null, branch =>
{
    branch.UseMiddleware<AuthMiddleware>();
    branch.UseMiddleware<RateLimitMiddleware>();
    branch.Run(proxy.HandleAsync);
});

app.Run();
