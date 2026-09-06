using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure.Auth;

public static class AuthRegistration
{
    /// <summary>
    /// Wires whichever credential source is configured. Only the validator changes between providers —
    /// token issuing, claims and authorization stay identical, so moving from local accounts to a
    /// corporate directory is a configuration change, not a rewrite.
    /// </summary>
    /// <summary>
    /// The issuer is created by the caller so the token-validation parameters can reference the very
    /// same key object; building a throwaway service provider to fetch it would be a second container.
    /// </summary>
    public static IServiceCollection AddAuth(
        this IServiceCollection services, AuthOptions options, JwtTokenIssuer? issuer)
    {
        services.AddSingleton(options);
        services.AddScoped<ICredentialStore, CredentialStore>();
        services.AddScoped<IAuthStore, AuthStore>();
        services.AddScoped<IHandleStore, PostgresHandleStore>();

        // Registered whether or not authentication is on: an operator can prepare accounts and
        // invitations before switching it on, and the settings screen reads this state either way.
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<ITotpService, TotpService>();
        services.AddScoped<IPasskeyService, PasskeyService>();

        if (options.Oidc.IsInteractive)
        {
            // A short timeout: discovery and the token endpoint are on the critical path of a sign-in,
            // and a provider that has stopped answering should fail fast rather than hang the browser.
            services.AddHttpClient("oidc", http => http.Timeout = TimeSpan.FromSeconds(15));
            services.AddScoped<OidcService>();
        }

        if (!options.Enabled || issuer is null) return services;

        services.AddSingleton(issuer);
        services.AddSingleton<ITokenIssuer>(issuer);

        switch (options.Provider)
        {
            case AuthProvider.Ldap:
                services.AddScoped<IIdentityValidator, LdapIdentityValidator>();
                break;
            case AuthProvider.Oidc:
                services.AddSingleton<IIdentityValidator, ExternalIdentityValidator>();
                break;
            default:
                services.AddScoped<IIdentityValidator, LocalIdentityValidator>();
                break;
        }

        return services;
    }
}

/// <summary>
/// Creates the first administrator, once, from configuration supplied at deploy time. ResolveDesk ships
/// with no default account and no default password: if nothing is configured, nothing is created, and
/// the operator is told how to create one.
/// </summary>
public static class AuthBootstrap
{
    public static async Task EnsureAdminAsync(IServiceProvider sp, ILogger logger, CancellationToken ct = default)
    {
        var options = sp.GetRequiredService<AuthOptions>();
        if (!options.Enabled) return;

        if (options.Provider is not AuthProvider.Local)
        {
            logger.LogInformation("Auth provider is {Provider}; local admin bootstrap skipped.", options.Provider);
            return;
        }

        var email = options.BootstrapAdminEmail;
        var password = options.BootstrapAdminPassword;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Authentication is on but no administrator exists yet. Set Auth__BootstrapAdminEmail and " +
                "Auth__BootstrapAdminPassword in the environment and restart to create one.");
            return;
        }

        using var scope = sp.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICredentialStore>();

        var existing = await store.FindByEmailAsync(email, ct);
        if (existing is { PasswordHash: not null })
        {
            logger.LogInformation("Bootstrap administrator already present; nothing to do.");
            return;
        }

        var user = existing is { } row
            ? new AuthenticatedUser(row.Id, row.FullName, row.Email, row.Role)
            : await store.UpsertDirectoryUserAsync(email, "Administrator", UserRole.Admin, ct);

        await store.SetPasswordHashAsync(user.Id, PasswordHasher.Hash(password), ct);
        logger.LogWarning(
            "Bootstrap administrator {Email} created. Change this password after the first login and " +
            "remove Auth__BootstrapAdminPassword from the environment.", email);
    }
}
