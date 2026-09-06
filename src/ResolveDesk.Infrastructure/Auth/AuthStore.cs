using Dapper;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>
/// Storage for the credentials the built-in sign-in methods own: setup invitations, TOTP secrets and
/// WebAuthn public keys. Directory and OIDC accounts keep their secrets in their own identity system
/// and appear here only as a user row with no password.
/// </summary>
public sealed class AuthStore(DbConnectionFactory factory) : IAuthStore
{
    private const string InvitationCols =
        @"i.id AS ""Id"", i.user_id AS ""UserId"", u.email AS ""Email"", u.full_name AS ""FullName"",
          u.role AS ""Role"", i.expires_at_utc AS ""ExpiresAtUtc"",
          i.consumed_at_utc AS ""ConsumedAtUtc"", i.created_at_utc AS ""CreatedAtUtc""";

    public async Task<long> CreateInvitedUserAsync(UserCreate user, CancellationToken ct = default)
    {
        // No password_hash: the account exists but cannot be signed into until the link is used.
        const string sql = """
            INSERT INTO users (full_name, email, role, skills) VALUES (@fullName, @email, @role, @skills)
            RETURNING id;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(sql, new
        {
            fullName = user.FullName, email = user.Email, role = (int)user.Role, skills = user.Skills,
        });
    }

    public async Task<long> SaveInvitationAsync(
        long userId, string tokenHash, DateTime expiresAtUtc, long createdBy, CancellationToken ct = default)
    {
        // Any previous live link is consumed first, so re-inviting never leaves two working ways in.
        const string sql = """
            UPDATE user_invitations SET consumed_at_utc = now()
            WHERE user_id = @userId AND consumed_at_utc IS NULL;

            INSERT INTO user_invitations (user_id, token_hash, expires_at_utc, created_by)
            VALUES (@userId, @tokenHash, @expiresAtUtc, @createdBy)
            RETURNING id;
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(sql,
            new { userId, tokenHash, expiresAtUtc, createdBy = createdBy == 0 ? (long?)null : createdBy });
    }

    public async Task<Invitation?> FindInvitationByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        // Expiry and prior use are part of the lookup, so a caller cannot forget to check them.
        var sql = $"""
            SELECT {InvitationCols}
            FROM user_invitations i JOIN users u ON u.id = i.user_id
            WHERE i.token_hash = @tokenHash
              AND i.consumed_at_utc IS NULL
              AND i.expires_at_utc > now()
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<Invitation>(sql, new { tokenHash });
    }

    public async Task ConsumeInvitationAsync(long invitationId, CancellationToken ct = default)
    {
        const string sql = "UPDATE user_invitations SET consumed_at_utc = now() WHERE id = @invitationId";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { invitationId });
    }

    public async Task<bool> RevokeInvitationsAsync(long userId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE user_invitations SET consumed_at_utc = now()
            WHERE user_id = @userId AND consumed_at_utc IS NULL
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteAsync(sql, new { userId }) > 0;
    }

    public async Task<IReadOnlyList<Invitation>> ListPendingInvitationsAsync(CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {InvitationCols}
            FROM user_invitations i JOIN users u ON u.id = i.user_id
            WHERE i.consumed_at_utc IS NULL
            ORDER BY i.created_at_utc DESC
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<Invitation>(sql)).ToList();
    }

    public async Task<string?> GetTotpSecretAsync(long userId, CancellationToken ct = default)
    {
        const string sql = "SELECT totp_secret FROM users WHERE id = @userId";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(sql, new { userId });
    }

    public async Task SetTotpSecretAsync(long userId, string? secret, bool enabled, CancellationToken ct = default)
    {
        const string sql = "UPDATE users SET totp_secret = @secret, totp_enabled = @enabled WHERE id = @userId";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { userId, secret, enabled });
    }

    public async Task<bool> IsTotpEnabledAsync(long userId, CancellationToken ct = default)
    {
        const string sql = "SELECT coalesce(totp_enabled, false) FROM users WHERE id = @userId";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(sql, new { userId });
    }

    public async Task<IReadOnlyList<PasskeySummary>> ListPasskeysAsync(long userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS "Id", label AS "Label", created_at_utc AS "CreatedAtUtc",
                   last_used_at_utc AS "LastUsedAtUtc"
            FROM user_passkeys WHERE user_id = @userId ORDER BY created_at_utc
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return (await conn.QueryAsync<PasskeySummary>(sql, new { userId })).ToList();
    }

    public async Task AddPasskeyAsync(
        long userId, byte[] credentialId, byte[] publicKey, long signCount, string label, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO user_passkeys (user_id, credential_id, public_key, sign_count, label)
            VALUES (@userId, @credentialId, @publicKey, @signCount, @label)
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { userId, credentialId, publicKey, signCount, label });
    }

    public async Task<StoredPasskey?> FindPasskeyAsync(byte[] credentialId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT p.id AS "Id", p.user_id AS "UserId", p.public_key AS "PublicKey",
                   p.sign_count AS "SignCount", u.full_name AS "FullName", u.email AS "Email", u.role AS "Role"
            FROM user_passkeys p JOIN users u ON u.id = p.user_id
            WHERE p.credential_id = @credentialId AND u.is_active
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<StoredPasskey>(sql, new { credentialId });
    }

    public async Task TouchPasskeyAsync(long passkeyId, long signCount, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE user_passkeys SET sign_count = @signCount, last_used_at_utc = now() WHERE id = @passkeyId
            """;
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { passkeyId, signCount });
    }

    public async Task<bool> DeletePasskeyAsync(long userId, long passkeyId, CancellationToken ct = default)
    {
        // Scoped to the owner: an id from someone else's account matches nothing.
        const string sql = "DELETE FROM user_passkeys WHERE id = @passkeyId AND user_id = @userId";
        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteAsync(sql, new { userId, passkeyId }) > 0;
    }
}
