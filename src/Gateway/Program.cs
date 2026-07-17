using Gateway;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var configPath = builder.Configuration["GATEWAY_CONFIG"] ?? "gateway.json";
var config = GatewayConfig.Load(configPath);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(new RouteTable(config));
builder.Services.AddSingleton<ProxyHandler>();
builder.Services.AddHostedService<HealthMonitor>();

var app = builder.Build();

app.Logger.LogInformation("Gateway routes: {Routes}",
    string.Join(", ", config.Routes.Select(r =>
        $"{r.PathPrefix} -> {r.Pool} [{config.Pools[r.Pool].Count} backend(s)]")));

app.MapMetrics();

// Middleware registered here would swallow requests before the implicit
// endpoint dispatch runs, so the proxy explicitly yields to any matched
// gateway endpoint (/metrics) and handles everything else itself.
var proxy = app.Services.GetRequiredService<ProxyHandler>();
app.Use(async (context, next) =>
{
    if (context.GetEndpoint() is not null)
    {
        await next(context);
        return;
    }

    await proxy.HandleAsync(context);
});

app.Run();
