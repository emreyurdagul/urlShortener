using Dapper;
using Npgsql;

namespace Monolith;

/// <summary>Data-access layer for the users table (the old auth-service's DB work).</summary>
public sealed class UserRepository(NpgsqlDataSource db)
{
    public async Task<UserRow?> FindByEmailAsync(string email)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<UserRow>(
            "SELECT id AS Id, email AS Email, password_hash AS PasswordHash, plan AS Plan FROM users WHERE email = @email",
            new { email });
    }

    /// <summary>Inserts a user and returns the new id. Throws a unique-violation on a duplicate email.</summary>
    public async Task<long> InsertAsync(string email, string passwordHash)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "INSERT INTO users (email, password_hash) VALUES (@email, @hash) RETURNING id",
            new { email, hash = passwordHash });
    }

    public async Task<int> UpdatePlanAsync(long id, string plan)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.ExecuteAsync(
            "UPDATE users SET plan = @plan WHERE id = @id", new { plan, id });
    }
}
