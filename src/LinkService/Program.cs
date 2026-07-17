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

var linksCreated = Metrics.CreateCounter(
    "links_created_total", "Short links created.",
    new CounterConfiguration { LabelNames = ["domain"] });

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

    for (var attempt = 0; attempt < 5; attempt++)
    {
        var code = CodeGenerator.Generate(codeLength);
        try
        {
            await using var conn = await db.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "INSERT INTO links (domain, code, target_url) VALUES (@domain, @code, @targetUrl)",
                new { domain, code, targetUrl = target.AbsoluteUri });

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

app.MapGet("/{code}", async (string code, NpgsqlDataSource db, HttpRequest http) =>
{
    // The gateway carries the public hostname in X-Forwarded-Host; that hostname
    // is the domain a short code lives under.
    var host = http.Headers["X-Forwarded-Host"].FirstOrDefault() ?? http.Host.Value ?? "";

    await using var conn = await db.OpenConnectionAsync();
    var target = await conn.QueryFirstOrDefaultAsync<string>(
        "SELECT target_url FROM links WHERE domain = @domain AND code = @code",
        new { domain = host.ToLowerInvariant(), code });

    return target is null ? Results.NotFound() : Results.Redirect(target);
});

app.Run();

public sealed record CreateLinkRequest(string Url, string? Domain, int? CodeLength);
