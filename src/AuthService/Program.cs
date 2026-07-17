using System.Security.Cryptography;
using AuthService;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Db")
    ?? "Host=localhost;Username=shortener;Password=shortener;Database=shortener";
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<UserRow>, PasswordHasher<UserRow>>();

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
        return Results.BadRequest(new { error = "A valid email is required." });
    if (req.Password is not { Length: >= 8 })
        return Results.BadRequest(new { error = "Password must be at least 8 characters." });

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
        return Results.Conflict(new { error = "Email already registered." });
    }

    var (token, expiresAt) = tokens.Issue(userId, email, Plans.Free, DateTime.UtcNow);
    return Results.Ok(new { token, expiresAt, plan = Plans.Free });
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
        return Results.Unauthorized();

    var (token, expiresAt) = tokens.Issue(user.Id, user.Email, user.Plan, DateTime.UtcNow);
    return Results.Ok(new { token, expiresAt, plan = user.Plan });
});

// Phase-6 payment flow will drive this; for now it lets the tier machinery
// be exercised end-to-end.
app.MapPost("/api/auth/plan", async (SetPlanRequest req, NpgsqlDataSource db) =>
{
    if (!Plans.IsValid(req.Plan))
        return Results.BadRequest(new { error = "Unknown plan." });

    await using var conn = await db.OpenConnectionAsync();
    var updated = await conn.ExecuteAsync(
        "UPDATE users SET plan = @plan WHERE id = @id",
        new { plan = req.Plan, id = req.UserId });

    return updated == 0 ? Results.NotFound() : Results.Ok(new { req.UserId, req.Plan });
});

app.Run();

public sealed record Credentials(string Email, string Password);
public sealed record SetPlanRequest(long UserId, string Plan);
