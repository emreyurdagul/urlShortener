using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace Monolith;

/// <summary>
/// Application/business layer for authentication. The old auth-service endpoints
/// (register / login / plan) become plain methods here — a monolith replaces an
/// HTTP call to another service with an in-process method call.
///
/// Registered as a singleton so the timing-attack dummy hash is computed once at
/// startup, not per request (hashing is deliberately slow).
/// </summary>
public sealed class AuthAppService
{
    private readonly UserRepository _users;
    private readonly IPasswordHasher<UserRow> _hasher;
    private readonly JwtService _jwt;
    private readonly string _dummyHash;

    public AuthAppService(UserRepository users, IPasswordHasher<UserRow> hasher, JwtService jwt)
    {
        _users = users;
        _hasher = hasher;
        _jwt = jwt;
        // Precomputed hash of a random password; verifying against it on an
        // unknown-email login costs the same as a real check, so response timing
        // does not reveal which emails exist.
        _dummyHash = hasher.HashPassword(
            new UserRow(0, "", "", Plans.Free),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
    }

    public sealed record AuthResult(string Token, DateTime ExpiresAt, string Plan);

    /// <summary>Returns null when the email is already registered.</summary>
    public async Task<AuthResult?> RegisterAsync(string email, string password)
    {
        email = email.Trim().ToLowerInvariant();
        var hash = _hasher.HashPassword(new UserRow(0, email, "", Plans.Free), password);

        long userId;
        try
        {
            userId = await _users.InsertAsync(email, hash);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }

        var (token, expiresAt) = _jwt.Issue(userId, email, Plans.Free, DateTime.UtcNow);
        return new AuthResult(token, expiresAt, Plans.Free);
    }

    /// <summary>Returns null on bad credentials.</summary>
    public async Task<AuthResult?> LoginAsync(string? email, string? password)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        var user = await _users.FindByEmailAsync(email);

        // Verify even when the user is missing so response timing doesn't reveal
        // which emails exist.
        var reference = user ?? new UserRow(0, email, _dummyHash, Plans.Free);
        var result = _hasher.VerifyHashedPassword(reference, reference.PasswordHash, password ?? "");
        if (user is null || result == PasswordVerificationResult.Failed)
            return null;

        var (token, expiresAt) = _jwt.Issue(user.Id, user.Email, user.Plan, DateTime.UtcNow);
        return new AuthResult(token, expiresAt, user.Plan);
    }

    /// <summary>
    /// Upgrades an authenticated user's tier and returns a FRESH token carrying
    /// the new plan (JWTs are stateless, so the old token would keep reading as
    /// the old plan). Returns null if the user no longer exists. Payment is
    /// simulated — no charge — but the DB + token flow is real.
    /// </summary>
    public async Task<AuthResult?> UpgradeAsync(long userId, string plan)
    {
        var user = await _users.UpdatePlanReturningAsync(userId, Plans.Normalize(plan));
        if (user is null)
            return null;

        var (token, expiresAt) = _jwt.Issue(user.Id, user.Email, user.Plan, DateTime.UtcNow);
        return new AuthResult(token, expiresAt, user.Plan);
    }
}
