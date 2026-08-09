using Dapper;
using LinkService;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? "Host=localhost;Username=shortener;Password=shortener;Database=shortener";
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

var configuredDomains = (builder.Configuration["LINK_DOMAINS"] ?? "localhost:8080")
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
builder.Services.AddSingleton(new DomainConfig(configuredDomains));
builder.Services.AddSingleton<LinkCache>();
builder.Services.AddSingleton<ClickRecorder>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<ClickShipper>();
builder.Services.AddHostedService<CacheInvalidator>();

// Distributed tracing: server spans + outgoing HTTP (to analytics) + Postgres
// spans, exported over OTLP (endpoint from OTEL_EXPORTER_OTLP_ENDPOINT) to Tempo.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("link-service"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
            !ctx.Request.Path.StartsWithSegments("/health") &&
            !ctx.Request.Path.StartsWithSegments("/metrics"))
        .AddHttpClientInstrumentation()
        .AddNpgsql())
    .UseOtlpExporter();

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

// Powers the domain dropdown in the UI.
app.MapGet("/api/domains", (DomainConfig domains) =>
    Results.Ok(new { domains = domains.All, @default = domains.Default }));

// The caller's own links; the gateway supplies the trusted owner id.
app.MapGet("/api/links", async (NpgsqlDataSource db, HttpRequest http, int? page, int? pageSize) =>
{
    if (!long.TryParse(http.Headers[IdentityHeaders.UserId].FirstOrDefault(), out var owner))
        return Results.Unauthorized();

    var p = Math.Max(1, page ?? 1);
    var size = Math.Clamp(pageSize ?? 20, 1, 100);
    var scheme = http.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? http.Scheme;
    await using var conn = await db.OpenConnectionAsync();

    var total = await conn.ExecuteScalarAsync<long>(
        "SELECT COUNT(*) FROM links WHERE owner_id = @owner", new { owner });
    var rows = await conn.QueryAsync<LinkRow>(
        """
        SELECT domain AS Domain, code AS Code, target_url AS TargetUrl, created_at AS CreatedAt
        FROM links WHERE owner_id = @owner ORDER BY created_at DESC
        LIMIT @size OFFSET @offset
        """,
        new { owner, size, offset = (p - 1) * size });

    var links = rows.Select(r => new
    {
        r.Code,
        r.Domain,
        r.TargetUrl,
        r.CreatedAt,
        shortUrl = $"{scheme}://{r.Domain}/{r.Code}",
        qrUrl = $"/api/links/{r.Code}/qr",
    });
    return Results.Ok(new { links, page = p, pageSize = size, total, hasMore = (long)p * size < total });
});

// A PNG QR code for the short URL. Rendered with QRCoder's pure-managed PNG
// encoder, so no System.Drawing dependency is needed in the container.
app.MapGet("/api/links/{code}/qr", async (string code, NpgsqlDataSource db, DomainConfig domains, HttpRequest http) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var link = await conn.QueryFirstOrDefaultAsync<(string Domain, string Code)>(
        "SELECT domain, code FROM links WHERE code = @code LIMIT 1", new { code });
    if (link.Domain is null)
        return Results.NotFound();

    var scheme = http.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? http.Scheme;
    var shortUrl = $"{scheme}://{link.Domain}/{link.Code}";
    var png = QrGenerator.Png(shortUrl);
    return Results.File(png, "image/png");
});

app.MapPost("/api/links", async (CreateLinkRequest req, NpgsqlDataSource db, DomainConfig domains, HttpRequest http) =>
{
    if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var target) ||
        target.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { code = "url_invalid", error = "URL must be an absolute http(s) URL." });
    if (!await LinkSafety.IsPublicAsync(target))
        return Results.BadRequest(new { code = "url_unsafe", error = "That URL points to a private or unreachable host." });

    var domain = (req.Domain ?? domains.Default).ToLowerInvariant();
    if (!domains.Contains(domain))
        return Results.BadRequest(new { code = "domain_invalid", error = $"Domain '{domain}' is not served here.", domain });

    // The gateway authenticates the caller and passes trusted identity headers;
    // their absence means an anonymous request (free tier, cannot own links).
    long? ownerId = long.TryParse(http.Headers[IdentityHeaders.UserId].FirstOrDefault(), out var uid) ? uid : null;
    var plan = Plans.Normalize(http.Headers[IdentityHeaders.UserPlan].FirstOrDefault());
    var scheme = http.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? http.Scheme;

    // Plan quota applies to owned links (random or vanity alike).
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
                    new { code = "quota_exceeded", error = $"Plan '{plan}' allows {limit} links; upgrade for more.", plan, quota = limit, used = owned },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }
    }

    // Vanity code (Pro): the caller chose the code, so a collision is a hard 409
    // — it isn't ours to change — rather than a retry.
    if (!string.IsNullOrWhiteSpace(req.Code))
    {
        if (!Plans.AllowsVanity(plan))
            return Results.Json(new { code = "vanity_forbidden", error = "Custom codes are a Pro feature." },
                statusCode: StatusCodes.Status403Forbidden);
        if (!CodeGenerator.IsValidVanity(req.Code))
            return Results.BadRequest(new { code = "vanity_invalid", error = "Custom code must be 1-32 chars of letters, digits, '-' or '_'." });

        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "INSERT INTO links (domain, code, target_url, owner_id) VALUES (@domain, @code, @targetUrl, @ownerId)",
                new { domain, code = req.Code, targetUrl = target.AbsoluteUri, ownerId });
            linksCreated.WithLabels(domain).Inc();
            return Results.Created($"/api/links/{req.Code}",
                new { code = req.Code, domain, shortUrl = $"{scheme}://{domain}/{req.Code}" });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new { code = "code_taken", error = $"'{req.Code}' is already taken.", requested = req.Code });
        }
    }

    // Random code: the requested length must be within the caller's tier.
    var codeLength = req.CodeLength ?? 7;
    if (codeLength is < CodeGenerator.MinLength or > CodeGenerator.MaxLength)
        return Results.BadRequest(new
        {
            code = "code_length_range",
            error = $"codeLength must be between {CodeGenerator.MinLength} and {CodeGenerator.MaxLength}.",
            min = CodeGenerator.MinLength,
            max = CodeGenerator.MaxLength,
        });
    var minLength = Plans.MinCodeLength(plan);
    if (codeLength < minLength)
        return Results.Json(
            new { code = "code_length_locked", error = $"{codeLength}-char codes need a higher plan; '{plan}' starts at {minLength}.", plan, minLength },
            statusCode: StatusCodes.Status403Forbidden);

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

    return Results.Json(new { code = "code_alloc_failed", error = "Could not allocate a unique code, please try again." }, statusCode: 500);
});

// Delete a link the caller owns. Ownership is enforced in the WHERE clause
// (owner_id must match the trusted header), so 0 rows = not found or not yours.
app.MapDelete("/api/links/{code}", async (string code, string? domain, NpgsqlDataSource db, LinkCache cache, HttpRequest http) =>
{
    if (!long.TryParse(http.Headers[IdentityHeaders.UserId].FirstOrDefault(), out var owner))
        return Results.Json(new { code = "unauthenticated", error = "Sign in required." }, statusCode: StatusCodes.Status401Unauthorized);

    var dom = (domain ?? http.Headers["X-Forwarded-Host"].FirstOrDefault() ?? "").ToLowerInvariant();
    await using var conn = await db.OpenConnectionAsync();
    var rows = await conn.ExecuteAsync(
        "DELETE FROM links WHERE domain = @dom AND code = @code AND owner_id = @owner",
        new { dom, code, owner });
    if (rows == 0)
        return Results.NotFound(new { code = "link_not_found", error = "Link not found." });

    cache.Remove(dom, code);
    await CacheInvalidator.NotifyAsync(conn, dom, code);
    return Results.NoContent();
});

// Edit a link's destination (owner-only). Codes are immutable; only the target
// changes. The local cache is refreshed; other replicas expire on TTL.
app.MapPut("/api/links/{code}", async (string code, string? domain, UpdateLinkRequest req, NpgsqlDataSource db, LinkCache cache, HttpRequest http) =>
{
    if (!long.TryParse(http.Headers[IdentityHeaders.UserId].FirstOrDefault(), out var owner))
        return Results.Json(new { code = "unauthenticated", error = "Sign in required." }, statusCode: StatusCodes.Status401Unauthorized);
    if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { code = "url_invalid", error = "URL must be an absolute http(s) URL." });
    if (!await LinkSafety.IsPublicAsync(target))
        return Results.BadRequest(new { code = "url_unsafe", error = "That URL points to a private or unreachable host." });

    var dom = (domain ?? http.Headers["X-Forwarded-Host"].FirstOrDefault() ?? "").ToLowerInvariant();
    await using var conn = await db.OpenConnectionAsync();
    var rows = await conn.ExecuteAsync(
        "UPDATE links SET target_url = @target WHERE domain = @dom AND code = @code AND owner_id = @owner",
        new { target = target.AbsoluteUri, dom, code, owner });
    if (rows == 0)
        return Results.NotFound(new { code = "link_not_found", error = "Link not found." });

    cache.Remove(dom, code);
    await CacheInvalidator.NotifyAsync(conn, dom, code);
    return Results.Ok(new { code, domain = dom, targetUrl = target.AbsoluteUri });
});

app.MapGet("/{code}", async (string code, NpgsqlDataSource db, LinkCache cache, ClickRecorder clicks, HttpRequest http) =>
{
    // The gateway carries the public hostname in X-Forwarded-Host; that hostname
    // is the domain a short code lives under.
    var domain = (http.Headers["X-Forwarded-Host"].FirstOrDefault() ?? http.Host.Value ?? "").ToLowerInvariant();

    // Hot path: immutable code -> target, so most hits never touch Postgres.
    if (!cache.TryGet(domain, code, out var target))
    {
        await using var conn = await db.OpenConnectionAsync();
        var found = await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT target_url FROM links WHERE domain = @domain AND code = @code",
            new { domain, code });

        if (found is null)
            return Results.NotFound();

        target = found;
        cache.Set(domain, code, target);
    }

    // Fire-and-forget: never let click bookkeeping slow the redirect.
    clicks.TryRecord(new ClickEvent(
        domain, code,
        http.Headers.Referer.FirstOrDefault(),
        http.Headers.UserAgent.FirstOrDefault(),
        DateTime.UtcNow));

    return Results.Redirect(target);
});

app.Run();

public sealed record CreateLinkRequest(string Url, string? Domain, int? CodeLength, string? Code);

public sealed record UpdateLinkRequest(string Url);

public sealed record LinkRow(string Domain, string Code, string TargetUrl, DateTime CreatedAt);

public static class IdentityHeaders
{
    public const string UserId = "X-User-Id";
    public const string UserPlan = "X-User-Plan";
}
