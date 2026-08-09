using AnalyticsService;
using Dapper;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? "Host=localhost;Username=shortener;Password=shortener;Database=shortener";
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

// Distributed tracing → OTLP → Tempo (endpoint from OTEL_EXPORTER_OTLP_ENDPOINT).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("analytics-service"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
            !ctx.Request.Path.StartsWithSegments("/health") &&
            !ctx.Request.Path.StartsWithSegments("/metrics"))
        .AddHttpClientInstrumentation()
        .AddNpgsql())
    .UseOtlpExporter();

var app = builder.Build();

var clicksIngested = Metrics.CreateCounter("analytics_clicks_ingested_total", "Click events written.");

app.UseHttpMetrics();
app.MapMetrics();

await Database.MigrateAsync(
    app.Services.GetRequiredService<NpgsqlDataSource>(),
    app.Logger,
    app.Lifetime.ApplicationStopping);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// link-service ships clicks here in batches off its redirect hot path.
app.MapPost("/api/clicks", async (ClickEvent[] events, NpgsqlDataSource db) =>
{
    if (events.Length == 0)
        return Results.Ok(new { ingested = 0 });

    await using var conn = await db.OpenConnectionAsync();
    await conn.ExecuteAsync(
        """
        INSERT INTO clicks (domain, code, referer, user_agent, clicked_at)
        VALUES (@Domain, @Code, @Referer, @UserAgent, @ClickedAt)
        """,
        events);

    clicksIngested.Inc(events.Length);
    return Results.Ok(new { ingested = events.Length });
});

app.MapGet("/api/analytics/{code}", async (string code, string? domain, NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var stats = await conn.QueryFirstOrDefaultAsync(
        """
        SELECT COUNT(*) AS total, MAX(clicked_at) AS last_click
        FROM clicks
        WHERE code = @code AND (@domain IS NULL OR domain = @domain)
        """,
        new { code, domain });

    return Results.Ok(new { code, total = (long)(stats?.total ?? 0L), lastClick = stats?.last_click });
});

app.Run();
