using Dapper;
using LinkService;
using Npgsql;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? "Host=localhost;Username=shortener;Password=shortener;Database=shortener";
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

var configuredDomains = (builder.Configuration["LINK_DOMAINS"] ?? "localhost:8080")
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddSingleton(new DomainConfig(configuredDomains));
builder.Services.AddSingleton<ClickRecorder>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<ClickShipper>();

var linksCreated = Metrics.CreateCounter(
    "links_created_total", "Short links created.",
    new CounterConfiguration { LabelNames = ["domain"] });
var quotaRejected = Metrics.CreateCounter(
    "links_quota_rejected_total", "Link creations rejected because the owner hit their plan quota.");

var app = builder.Build();

app.UseHttpMetrics();
app.MapMetrics();

await Database.MigrateAsync(
    app.Services.GetRequiredService<NpgsqlDataSource>(),
    app.Logger,
    app.Lifetime.ApplicationStopping);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/links", async (CreateLinkRequest req, NpgsqlDataSource db, DomainConfig domains, HttpRequest http) =>
{
    if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var target) ||
        target.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { error = "url must be an absolute http(s) URL." });

    var codeLength = req.CodeLength ?? 7;
    if (codeLength is < CodeGenerator.MinLength or > CodeGenerator.MaxLength)
        return Results.BadRequest(new
        {
            error = $"codeLength must be between {CodeGenerator.MinLength} and {CodeGenerator.MaxLength}.",
        });

    var domain = (req.Domain ?? domains.Default).ToLowerInvariant();
    if (!domains.Contains(domain))
        return Results.BadRequest(new { error = $"domain '{domain}' is not served here." });

    // The gateway authenticates the caller and passes trusted identity headers;
    // their absence means an anonymous request, which cannot own links.
    long? ownerId = long.TryParse(http.Headers[IdentityHeaders.UserId].FirstOrDefault(), out var uid) ? uid : null;
    var plan = http.Headers[IdentityHeaders.UserPlan].FirstOrDefault() ?? "free";

    if (ownerId is { } owner)
    {
        var quota = Plans.LinkQuota(plan);
        if (quota is { } limit)
        {
            await using var countConn = await db.OpenConnectionAsync();
            var owned = await countConn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM links WHERE owner_id = @owner", new { owner });
            if (owned >= limit)
            {
                quotaRejected.Inc();
                return Results.Json(
                    new { error = $"Plan '{plan}' allows {limit} links; upgrade for more.", quota = limit, used = owned },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }
    }

    for (var attempt = 0; attempt < 5; attempt++)
    {
        var code = CodeGenerator.Generate(codeLength);
        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "INSERT INTO links (domain, code, target_url, owner_id) VALUES (@domain, @code, @targetUrl, @ownerId)",
                new { domain, code, targetUrl = target.AbsoluteUri, ownerId });

            linksCreated.WithLabels(domain).Inc();
            var scheme = http.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? http.Scheme;
            return Results.Created($"/api/links/{code}", new
            {
                code,
                domain,
                shortUrl = $"{scheme}://{domain}/{code}",
            });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Random code collided with an existing one; retry with a fresh code.
        }
    }

    return Results.Problem("Could not allocate a unique code, please try again.", statusCode: 500);
});

app.MapGet("/{code}", async (string code, NpgsqlDataSource db, ClickRecorder clicks, HttpRequest http) =>
{
    // The gateway carries the public hostname in X-Forwarded-Host; that hostname
    // is the domain a short code lives under.
    var domain = (http.Headers["X-Forwarded-Host"].FirstOrDefault() ?? http.Host.Value ?? "").ToLowerInvariant();

    await using var conn = await db.OpenConnectionAsync();
    var target = await conn.QueryFirstOrDefaultAsync<string>(
        "SELECT target_url FROM links WHERE domain = @domain AND code = @code",
        new { domain, code });

    if (target is null)
        return Results.NotFound();

    // Fire-and-forget: never let click bookkeeping slow the redirect.
    clicks.TryRecord(new ClickEvent(
        domain, code,
        http.Headers.Referer.FirstOrDefault(),
        http.Headers.UserAgent.FirstOrDefault(),
        DateTime.UtcNow));

    return Results.Redirect(target);
});

app.Run();

public sealed record CreateLinkRequest(string Url, string? Domain, int? CodeLength);

public static class IdentityHeaders
{
    public const string UserId = "X-User-Id";
    public const string UserPlan = "X-User-Plan";
}
