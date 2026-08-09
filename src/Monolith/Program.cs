using Microsoft.AspNetCore.Identity;
using Monolith;
using Npgsql;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? "Host=localhost;Username=shortener;Password=shortener;Database=shortener";
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

var configuredDomains = (builder.Configuration["LINK_DOMAINS"] ?? "localhost:8090")
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddSingleton(new DomainConfig(configuredDomains));

// Domain / cross-cutting singletons.
builder.Services.AddSingleton<LinkCache>();
builder.Services.AddSingleton<ClickRecorder>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<IPasswordHasher<UserRow>, PasswordHasher<UserRow>>();

var rateCapacity = builder.Configuration.GetValue("RATE_LIMIT_BURST", 60);
var rateRefill = builder.Configuration.GetValue("RATE_LIMIT_PER_SECOND", 30.0);
builder.Services.AddSingleton(new TokenBucketRateLimiter(rateCapacity, rateRefill));

// Data layer (repositories) — stateless over the singleton data source.
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<LinkRepository>();
builder.Services.AddSingleton<ClickRepository>();

// Application layer (services).
builder.Services.AddSingleton<AuthAppService>();
builder.Services.AddSingleton<LinkAppService>();
builder.Services.AddSingleton<AnalyticsAppService>();

// Background click writer: drains the channel to Postgres, off the hot path.
builder.Services.AddHostedService<ClickWriter>();

builder.Services.AddControllers();

var app = builder.Build();

app.Logger.LogInformation("Monolith serving domains: {Domains}",
    string.Join(", ", configuredDomains));

app.UseHttpMetrics();
app.MapMetrics();

await Database.MigrateAsync(
    app.Services.GetRequiredService<NpgsqlDataSource>(),
    app.Logger,
    app.Lifetime.ApplicationStopping);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Auth then rate limiting run for everything except infra endpoints
// (/metrics, /health) — the same pipeline shape the gateway had, now just
// middleware inside the one process instead of a separate network hop.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/metrics")
        && !ctx.Request.Path.StartsWithSegments("/health"),
    branch =>
    {
        branch.UseMiddleware<AuthMiddleware>();
        branch.UseMiddleware<RateLimitMiddleware>();
    });

app.MapControllers();

app.Run();

// Exposes the implicit top-level Program to WebApplicationFactory for
// integration tests (tests/Monolith.IntegrationTests).
public partial class Program;
