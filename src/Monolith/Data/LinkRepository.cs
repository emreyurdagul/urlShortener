using Dapper;
using Npgsql;

namespace Monolith;

/// <summary>Data-access layer for the links table (the old link-service's DB work).</summary>
public sealed class LinkRepository(NpgsqlDataSource db)
{
    public async Task<IReadOnlyList<LinkRow>> ListByOwnerAsync(long ownerId)
    {
        await using var conn = await db.OpenConnectionAsync();
        var rows = await conn.QueryAsync<LinkRow>(
            """
            SELECT domain AS Domain, code AS Code, target_url AS TargetUrl, created_at AS CreatedAt
            FROM links WHERE owner_id = @ownerId ORDER BY created_at DESC
            """,
            new { ownerId });
        return rows.ToList();
    }

    public async Task<long> CountByOwnerAsync(long ownerId)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM links WHERE owner_id = @ownerId", new { ownerId });
    }

    public async Task<string?> FindTargetAsync(string domain, string code)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<string?>(
            "SELECT target_url FROM links WHERE domain = @domain AND code = @code",
            new { domain, code });
    }

    public async Task<(string Domain, string Code)> FindForQrAsync(string code)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<(string Domain, string Code)>(
            "SELECT domain, code FROM links WHERE code = @code LIMIT 1", new { code });
    }

    /// <summary>
    /// Inserts a link. Throws <see cref="PostgresException"/> with SqlState
    /// <see cref="PostgresErrorCodes.UniqueViolation"/> when (domain, code)
    /// collides, which the caller uses to retry with a fresh code.
    /// </summary>
    public async Task InsertAsync(string domain, string code, string targetUrl, long? ownerId)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "INSERT INTO links (domain, code, target_url, owner_id) VALUES (@domain, @code, @targetUrl, @ownerId)",
            new { domain, code, targetUrl, ownerId });
    }
}
