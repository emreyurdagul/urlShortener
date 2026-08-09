using Dapper;
using Npgsql;

namespace Monolith;

/// <summary>Data-access for refresh tokens (stored hashed; rotated + revocable).</summary>
public sealed class RefreshTokenRepository(NpgsqlDataSource db)
{
    public async Task IssueAsync(long userId, string hash, DateTime expiresAt)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO refresh_tokens (user_id, token_hash, expires_at) VALUES (@userId, @hash, @expiresAt)",
            new { userId, hash, expiresAt });
    }

    /// <summary>Returns the token id + owner for a live (unrevoked, unexpired) token.</summary>
    public async Task<(long TokenId, long UserId, string Email, string Plan)?> FindActiveAsync(string hash)
    {
        await using var conn = await db.OpenConnectionAsync();
        var row = await conn.QueryFirstOrDefaultAsync(
            """
            SELECT rt.id AS tokenid, u.id AS userid, u.email AS email, u.plan AS plan
            FROM refresh_tokens rt JOIN users u ON u.id = rt.user_id
            WHERE rt.token_hash = @hash AND rt.revoked_at IS NULL AND rt.expires_at > now()
            """, new { hash });
        if (row is null)
            return null;
        return ((long)row.tokenid, (long)row.userid, (string)row.email, (string)row.plan);
    }

    public async Task RevokeByIdAsync(long id)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync("UPDATE refresh_tokens SET revoked_at = now() WHERE id = @id", new { id });
    }

    public async Task RevokeByHashAsync(string hash)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = now() WHERE token_hash = @hash AND revoked_at IS NULL",
            new { hash });
    }
}
