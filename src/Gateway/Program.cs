using Gateway;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var configPath = builder.Configuration["GATEWAY_CONFIG"] ?? "gateway.json";
var config = GatewayConfig.Load(configPath);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(new RouteTable(config));
builder.Services.AddSingleton<ProxyHandler>();
builder.Services.AddSingleton<JwtValidator>();
builder.Services.AddHostedService<HealthMonitor>();

var rateCapacity = builder.Configuration.GetValue("RATE_LIMIT_BURST", 60);
var rateRefill = builder.Configuration.GetValue("RATE_LIMIT_PER_SECOND", 30.0);
builder.Services.AddSingleton(new TokenBucketRateLimiter(rateCapacity, rateRefill));

var app = builder.Build();

app.Logger.LogInformation("Gateway routes: {Routes}",
    string.Join(", ", config.Routes.Select(r =>
        $"{r.PathPrefix} -> {r.Pool} [{config.Pools[r.Pool].Count} backend(s)]")));

app.UseRouting();
app.MapMetrics();

// Proxied traffic passes auth then rate limiting before reaching a backend;
// the gateway's own endpoints (/metrics) bypass both.
var proxy = app.Services.GetRequiredService<ProxyHandler>();
app.UseWhen(context => context.GetEndpoint() is null, branch =>
{
    branch.UseMiddleware<AuthMiddleware>();
    branch.UseMiddleware<RateLimitMiddleware>();
    branch.Run(proxy.HandleAsync);
});

app.Run();
