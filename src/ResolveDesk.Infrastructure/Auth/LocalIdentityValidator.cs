using Microsoft.Extensions.Logging;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>Validates against ResolveDesk's own users table. The default when no directory is configured.</summary>
public sealed class LocalIdentityValidator(ICredentialStore store, ILogger<LocalIdentityValidator> logger)
    : IIdentityValidator
{
    public AuthProvider Provider => AuthProvider.Local;

    public async Task<AuthenticatedUser?> ValidateAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await store.FindByEmailAsync(username, ct);

        // Hash a throwaway value when the account does not exist, so a missing account and a wrong
        // password take the same time and cannot be told apart by timing.
        if (user is null)
        {
            PasswordHasher.Verify(password, PasswordHasher.Hash("timing-equalizer"));
            logger.LogInformation("Local login rejected for an unknown or inactive account.");
            return null;
        }

        if (!PasswordHasher.Verify(password, user.Value.PasswordHash))
        {
            logger.LogInformation("Local login rejected for user {UserId}.", user.Value.Id);
            return null;
        }

        return new AuthenticatedUser(user.Value.Id, user.Value.FullName, user.Value.Email, user.Value.Role);
    }
}

/// <summary>Used when <c>Auth:Provider</c> is Oidc: the external provider owns credentials entirely.</summary>
public sealed class ExternalIdentityValidator : IIdentityValidator
{
    public AuthProvider Provider => AuthProvider.Oidc;

    public Task<AuthenticatedUser?> ValidateAsync(string username, string password, CancellationToken ct = default) =>
        throw new InvalidOperationException(
            "Auth:Provider is Oidc — this API does not accept passwords. Obtain a token from the configured " +
            "identity provider and send it as a bearer token.");
}
