using System.Security.Cryptography;
using AuthService;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? "Host=localhost;Username=shortener;Password=shortener;Database=shortener";
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<UserRow>, PasswordHasher<UserRow>>();

// Distributed tracing → OTLP → Tempo (endpoint from OTEL_EXPORTER_OTLP_ENDPOINT).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("auth-service"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
            !ctx.Request.Path.StartsWithSegments("/health") &&
            !ctx.Request.Path.StartsWithSegments("/metrics"))
        .AddHttpClientInstrumentation()
        .AddNpgsql())
    .UseOtlpExporter();

var app = builder.Build();

// Precomputed hash of a random password; verifying against it on an
// unknown-email login spends the same time as a real check, so response
// timing doesn't reveal which emails exist.
var dummyHash = app.Services.GetRequiredService<IPasswordHasher<UserRow>>()
    .HashPassword(new UserRow(0, "", "", Plans.Free), Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));

app.UseHttpMetrics();
app.MapMetrics();

await Database.MigrateAsync(
    app.Services.GetRequiredService<NpgsqlDataSource>(),
    app.Logger,
    app.Lifetime.ApplicationStopping);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/auth/register", async (
    Credentials req, NpgsqlDataSource db, IPasswordHasher<UserRow> hasher, TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
        return Results.BadRequest(new { code = "email_invalid", error = "A valid email is required." });
    if (req.Password is not { Length: >= 8 })
        return Results.BadRequest(new { code = "password_short", error = "Password must be at least 8 characters.", min = 8 });

    var email = req.Email.Trim().ToLowerInvariant();
    var hash = hasher.HashPassword(new UserRow(0, email, "", Plans.Free), req.Password);

    await using var conn = await db.OpenConnectionAsync();
    long userId;
    try
    {
        userId = await conn.ExecuteScalarAsync<long>(
            "INSERT INTO users (email, password_hash) VALUES (@email, @hash) RETURNING id",
            new { email, hash });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new { code = "email_taken", error = "Email already registered." });
    }

    var (token, expiresAt) = tokens.Issue(userId, email, Plans.Free, DateTime.UtcNow);
    var refreshToken = await RefreshTokens.IssueAsync(conn, userId);
    return Results.Ok(new { token, expiresAt, plan = Plans.Free, refreshToken });
});

app.MapPost("/api/auth/login", async (
    Credentials req, NpgsqlDataSource db, IPasswordHasher<UserRow> hasher, TokenService tokens) =>
{
    var email = (req.Email ?? "").Trim().ToLowerInvariant();

    await using var conn = await db.OpenConnectionAsync();
    var user = await conn.QueryFirstOrDefaultAsync<UserRow>(
        "SELECT id AS Id, email AS Email, password_hash AS PasswordHash, plan AS Plan FROM users WHERE email = @email",
        new { email });

    // Verify even when the user is missing so response timing doesn't reveal
    // which emails exist.
    var reference = user ?? new UserRow(0, email, dummyHash, Plans.Free);
    var result = hasher.VerifyHashedPassword(reference, reference.PasswordHash, req.Password ?? "");
    if (user is null || result == PasswordVerificationResult.Failed)
        return Results.Json(new { code = "bad_credentials", error = "Wrong email or password." },
            statusCode: StatusCodes.Status401Unauthorized);

    var (token, expiresAt) = tokens.Issue(user.Id, user.Email, user.Plan, DateTime.UtcNow);
    var refreshToken = await RefreshTokens.IssueAsync(conn, user.Id);
    return Results.Ok(new { token, expiresAt, plan = user.Plan, refreshToken });
});

// Self-serve tier upgrade. Unlike the old admin-ish /api/auth/plan (which let
// ANYONE upgrade ANYONE for free), this upgrades the AUTHENTICATED caller: the
// gateway validates the bearer token and passes a trusted X-User-Id. Payment is
// simulated — no charge — but the flow is real: the DB is updated and a FRESH
// token carrying the new plan is returned (JWTs are stateless, so the old token
// would keep reading as the old plan until it is replaced).
app.MapPost("/api/auth/upgrade", async (UpgradeRequest req, NpgsqlDataSource db, TokenService tokens, HttpRequest http) =>
{
    if (!long.TryParse(http.Headers["X-User-Id"].FirstOrDefault(), out var userId))
        return Results.Json(new { code = "unauthenticated", error = "Sign in required." },
            statusCode: StatusCodes.Status401Unauthorized);

    var plan = Plans.Normalize(req.Plan);
    if (!Plans.IsValid(plan) || plan == Plans.Free)
        return Results.BadRequest(new { code = "plan_invalid", error = "Choose a paid tier: 'plus' or 'pro'." });

    await using var conn = await db.OpenConnectionAsync();
    var user = await conn.QueryFirstOrDefaultAsync<UserRow>(
        """
        UPDATE users SET plan = @plan WHERE id = @id
        RETURNING id AS Id, email AS Email, password_hash AS PasswordHash, plan AS Plan
        """,
        new { plan, id = userId });
    if (user is null)
        return Results.Json(new { code = "user_not_found", error = "Account not found." },
            statusCode: StatusCodes.Status404NotFound);

    var (token, expiresAt) = tokens.Issue(user.Id, user.Email, user.Plan, DateTime.UtcNow);
    return Results.Ok(new { token, expiresAt, plan = user.Plan });
});

// Exchange a refresh token for a new access token, rotating the refresh token
// (revoke the old, issue a new) so a leaked-then-used token works only once.
app.MapPost("/api/auth/refresh", async (RefreshRequest req, NpgsqlDataSource db, TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(req.RefreshToken))
        return Results.BadRequest(new { code = "refresh_invalid", error = "Missing refresh token." });

    var hash = RefreshTokens.Hash(req.RefreshToken);
    await using var conn = await db.OpenConnectionAsync();
    var row = await conn.QueryFirstOrDefaultAsync(
        """
        SELECT rt.id AS tokenid, u.id AS userid, u.email AS email, u.plan AS plan
        FROM refresh_tokens rt JOIN users u ON u.id = rt.user_id
        WHERE rt.token_hash = @hash AND rt.revoked_at IS NULL AND rt.expires_at > now()
        """, new { hash });
    if (row is null)
        return Results.Json(new { code = "refresh_invalid", error = "Invalid or expired refresh token." },
            statusCode: StatusCodes.Status401Unauthorized);

    long userId = row.userid;
    string email = row.email;
    string plan = row.plan;
    await conn.ExecuteAsync("UPDATE refresh_tokens SET revoked_at = now() WHERE id = @id", new { id = (long)row.tokenid });
    var refreshToken = await RefreshTokens.IssueAsync(conn, userId);
    var (token, expiresAt) = tokens.Issue(userId, email, plan, DateTime.UtcNow);
    return Results.Ok(new { token, expiresAt, plan, refreshToken });
});

// Revoke a refresh token (logout). Idempotent.
app.MapPost("/api/auth/logout", async (RefreshRequest req, NpgsqlDataSource db) =>
{
    if (!string.IsNullOrWhiteSpace(req.RefreshToken))
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = now() WHERE token_hash = @hash AND revoked_at IS NULL",
            new { hash = RefreshTokens.Hash(req.RefreshToken) });
    }
    return Results.NoContent();
});

app.Run();

public sealed record Credentials(string Email, string Password);
public sealed record UpgradeRequest(string Plan);
public sealed record RefreshRequest(string RefreshToken);
