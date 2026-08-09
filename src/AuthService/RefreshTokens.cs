using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace AuthService;

/// <summary>
/// Opaque refresh tokens. A cryptographically-random value is handed to the
/// client; only its SHA-256 hash is stored, so a database leak can't be replayed
/// to mint access tokens. Tokens are single-use (rotated on every refresh) and
/// revocable (logout / expiry), which is what makes the short-lived access tokens
/// safe to trust statelessly at the gateway.
/// </summary>
public static class RefreshTokens
{
    public static TimeSpan Lifetime { get; } = TimeSpan.FromDays(30);

    public static (string Raw, string Hash) New()
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>Issues a new refresh token for a user and stores its hash.</summary>
    public static async Task<string> IssueAsync(NpgsqlConnection conn, long userId)
    {
        var (raw, hash) = New();
        await conn.ExecuteAsync(
            "INSERT INTO refresh_tokens (user_id, token_hash, expires_at) VALUES (@userId, @hash, @exp)",
            new { userId, hash, exp = DateTime.UtcNow + Lifetime });
        return raw;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
