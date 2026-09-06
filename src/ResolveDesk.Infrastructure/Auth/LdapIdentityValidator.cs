using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.Extensions.Logging;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>
/// Validates credentials by binding to LDAP or Active Directory. ResolveDesk never stores the password
/// — a successful bind *is* the proof — and the directory stays authoritative for display name and
/// group membership, which map onto ResolveDesk roles.
///
/// Uses <c>System.DirectoryServices.Protocols</c>: the platform's own LDAP client, so it works on
/// Windows and Linux and adds no reflection that would break Native AOT.
/// </summary>
public sealed class LdapIdentityValidator(
    AuthOptions auth,
    ICredentialStore store,
    ILogger<LdapIdentityValidator> logger) : IIdentityValidator
{
    private readonly LdapOptions _o = auth.Ldap;

    public AuthProvider Provider => AuthProvider.Ldap;

    public async Task<AuthenticatedUser?> ValidateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_o.Host))
            throw new InvalidOperationException("Auth:Provider is Ldap but Auth:Ldap:Host is not configured.");

        // An empty password would be an unauthenticated bind, which most servers accept — never treat
        // that as a successful login.
        if (string.IsNullOrEmpty(password)) return null;

        // The LDAP client is synchronous; keep it off the request thread.
        var directory = await Task.Run(() => BindAndRead(username, password), ct);
        if (directory is null) return null;

        var role = MapRole(directory.Groups);
        var email = string.IsNullOrWhiteSpace(directory.Email) ? username : directory.Email;
        var name = string.IsNullOrWhiteSpace(directory.DisplayName) ? username : directory.DisplayName;

        if (!_o.AutoProvisionUsers)
        {
            var existing = await store.FindByEmailAsync(email, ct);
            if (existing is null)
            {
                logger.LogInformation("Directory login succeeded but auto-provisioning is off and no local user exists.");
                return null;
            }
            return new AuthenticatedUser(existing.Value.Id, existing.Value.FullName, existing.Value.Email, existing.Value.Role);
        }

        return await store.UpsertDirectoryUserAsync(email, name, role, ct);
    }

    private DirectoryIdentity? BindAndRead(string username, string password)
    {
        var identifier = new LdapDirectoryIdentifier(_o.Host, _o.Port);
        using var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = _o.UseSsl;

        var bindDn = string.Format(_o.BindDnTemplate, username);

        try
        {
            connection.Bind(new NetworkCredential(bindDn, password));
        }
        catch (LdapException ex)
        {
            // Code 49 is "invalid credentials" — an expected outcome, not an error worth alarming on.
            if (ex.ErrorCode == 49)
            {
                logger.LogInformation("Directory rejected the credentials.");
                return null;
            }
            logger.LogWarning(ex, "Directory bind failed against {Host}:{Port}.", _o.Host, _o.Port);
            throw;
        }

        if (string.IsNullOrWhiteSpace(_o.BaseDn))
            return new DirectoryIdentity(null, null, []);

        try
        {
            var request = new SearchRequest(
                _o.BaseDn,
                string.Format(_o.UserFilter, EscapeFilter(username)),
                SearchScope.Subtree,
                _o.EmailAttribute, _o.DisplayNameAttribute, _o.GroupAttribute);

            if (connection.SendRequest(request) is not SearchResponse { Entries.Count: > 0 } response)
                return new DirectoryIdentity(null, null, []);

            var entry = response.Entries[0];
            return new DirectoryIdentity(
                First(entry, _o.EmailAttribute),
                First(entry, _o.DisplayNameAttribute),
                All(entry, _o.GroupAttribute));
        }
        catch (DirectoryException ex)
        {
            // The bind already proved the identity; a failed attribute read only costs us the role map.
            logger.LogWarning(ex, "Directory search failed after a successful bind; defaulting to Agent.");
            return new DirectoryIdentity(null, null, []);
        }
    }

    private UserRole MapRole(IReadOnlyList<string> groups)
    {
        if (Matches(_o.AdminGroup)) return UserRole.Admin;
        if (Matches(_o.CoordinatorGroup)) return UserRole.Coordinator;
        return UserRole.Agent;

        bool Matches(string? needle) =>
            !string.IsNullOrWhiteSpace(needle) &&
            groups.Any(g => g.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string? First(SearchResultEntry entry, string attribute) =>
        entry.Attributes[attribute] is { Count: > 0 } a ? a[0]?.ToString() : null;

    private static string[] All(SearchResultEntry entry, string attribute) =>
        entry.Attributes[attribute] is { } a
            ? [.. Enumerable.Range(0, a.Count).Select(i => a[i]?.ToString() ?? "")]
            : [];

    /// <summary>RFC 4515 escaping — the username reaches a filter string, so it must not be able to alter it.</summary>
    private static string EscapeFilter(string value)
    {
        Span<char> specials = ['\\', '*', '(', ')', '\0'];
        if (value.AsSpan().IndexOfAny(specials) < 0) return value;

        var sb = new System.Text.StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => "\\5c",
                '*' => "\\2a",
                '(' => "\\28",
                ')' => "\\29",
                '\0' => "\\00",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    private sealed record DirectoryIdentity(string? Email, string? DisplayName, IReadOnlyList<string> Groups);
}
