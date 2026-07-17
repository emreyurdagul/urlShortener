using Gateway;

var builder = WebApplication.CreateBuilder(args);

var configPath = builder.Configuration["GATEWAY_CONFIG"] ?? "gateway.json";
var config = GatewayConfig.Load(configPath);

builder.Services.AddSingleton(new RouteTable(config));
builder.Services.AddSingleton<ProxyHandler>();

var app = builder.Build();

app.Logger.LogInformation("Gateway routes: {Routes}",
    string.Join(", ", config.Routes.Select(r =>
        $"{r.PathPrefix} -> {r.Pool} [{config.Pools[r.Pool].Count} backend(s)]")));

var proxy = app.Services.GetRequiredService<ProxyHandler>();
app.Run(proxy.HandleAsync);

app.Run();
