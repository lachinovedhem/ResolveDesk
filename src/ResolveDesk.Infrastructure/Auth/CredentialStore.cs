using Dapper;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>Password and directory-user persistence against the same <c>users</c> table the rest of the app uses.</summary>
public sealed class CredentialStore(DbConnectionFactory factory) : ICredentialStore
{
    public async Task<(long Id, string FullName, string Email, UserRole Role, string? PasswordHash)?> FindByEmailAsync(
        string email, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS "Id", full_name AS "FullName", email AS "Email", role AS "Role",
                   password_hash AS "PasswordHash"
            FROM users WHERE lower(email) = lower(@email) AND is_active;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<CredentialRow>(sql, new { email });
        return row is null ? null : (row.Id, row.FullName, row.Email, (UserRole)row.Role, row.PasswordHash);
    }

    public async Task SetPasswordHashAsync(long userId, string hash, CancellationToken ct = default)
    {
        const string sql = "UPDATE users SET password_hash = @hash WHERE id = @userId";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { userId, hash });
    }

    public async Task<AuthenticatedUser> UpsertDirectoryUserAsync(
        string email, string fullName, UserRole role, CancellationToken ct = default)
    {
        // The directory stays authoritative for name and role, so both are refreshed on every login.
        // password_hash is deliberately left NULL: a directory account has no local password.
        const string sql = """
            INSERT INTO users (full_name, email, role)
            VALUES (@fullName, @email, @role)
            ON CONFLICT (email) DO UPDATE
                SET full_name = EXCLUDED.full_name, role = EXCLUDED.role, is_active = true
            RETURNING id;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>(sql, new { fullName, email, role = (int)role });
        return new AuthenticatedUser(id, fullName, email, role);
    }

    /// <summary>Flat row shape; Dapper.AOT maps to concrete types, not tuples.</summary>
    public sealed record CredentialRow(long Id, string FullName, string Email, int Role, string? PasswordHash);
}
