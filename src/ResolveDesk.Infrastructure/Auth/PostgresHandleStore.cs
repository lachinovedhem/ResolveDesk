using System.Security.Cryptography;
using System.Text;
using Dapper;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>
/// Handles kept in PostgreSQL, which every deployment already has. Redis would be the conventional
/// choice for expiring keys, but it would be a second piece of infrastructure to run, secure and back
/// up for a handful of rows that live sixty seconds — the database is right here and transactional.
///
/// Two properties this has to get right:
///
/// <b>Atomic redemption.</b> <c>DELETE … RETURNING</c> is a single statement, so of two instances
/// racing on the same handle exactly one gets the payload. A read-then-delete would let both win.
///
/// <b>Nothing usable at rest.</b> Only the handle's SHA-256 hash is stored. These are bearer secrets:
/// a live MFA handle read out of a database dump would otherwise be a way past a password.
/// </summary>
public sealed class PostgresHandleStore(DbConnectionFactory factory) : IHandleStore
{
    public async Task<string> IssueAsync(
        string purpose, string payload, TimeSpan lifetime, CancellationToken ct = default)
    {
        var handle = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Expired rows are cleared on the way past. The volume is tiny and the index makes it cheap,
        // so this needs no scheduled job to stop the table growing forever.
        const string sql = """
            DELETE FROM auth_handles WHERE expires_at_utc < now();

            INSERT INTO auth_handles (handle_hash, purpose, payload, expires_at_utc)
            VALUES (@handleHash, @purpose, @payload, @expiresAtUtc);
            """;

        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            handleHash = Hash(handle),
            purpose,
            payload,
            expiresAtUtc = DateTime.UtcNow.Add(lifetime),
        });

        return handle;
    }

    public async Task<string?> ConsumeAsync(string purpose, string handle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(handle)) return null;

        // The purpose is part of the WHERE clause, so a handle cannot be redeemed by the wrong flow.
        // Expiry is too, so a caller cannot forget to check it.
        const string sql = """
            DELETE FROM auth_handles
            WHERE handle_hash = @handleHash AND purpose = @purpose AND expires_at_utc > now()
            RETURNING payload;
            """;

        await using var conn = factory.Create();
        await conn.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(sql, new { handleHash = Hash(handle), purpose });
    }

    private static string Hash(string handle) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(handle))).ToLowerInvariant();
}
