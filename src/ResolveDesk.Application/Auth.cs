using Microsoft.Extensions.Configuration;
using ResolveDesk.Core;

namespace ResolveDesk.Application;

/// <summary>Where user credentials are checked. The token the API issues is the same either way.</summary>
public enum AuthProvider
{
    /// <summary>Passwords stored in ResolveDesk's own users table, hashed with PBKDF2.</summary>
    Local = 0,
    /// <summary>LDAP or Active Directory bind. ResolveDesk never stores the password.</summary>
    Ldap = 1,
    /// <summary>An external OpenID Connect provider issues the token; this API only validates it.</summary>
    Oidc = 2,
}

public sealed record JwtOptions
{
    public string Issuer { get; init; } = "resolvedesk";
    public string Audience { get; init; } = "resolvedesk";
    /// <summary>Never committed. Absent in Development means an ephemeral key; absent in Production is fatal.</summary>
    public string? SigningKey { get; init; }
    public int LifetimeMinutes { get; init; } = 480;
}

/// <summary>
/// Directory settings. Nothing here is defaulted to a real host — an operator supplies their own
/// directory, and until they do the provider simply is not selected.
/// </summary>
public sealed record LdapOptions
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 636;
    public bool UseSsl { get; init; } = true;
    /// <summary>Search root, e.g. <c>DC=example,DC=org</c>.</summary>
    public string BaseDn { get; init; } = "";
    /// <summary>How a username becomes a bind DN. <c>{0}</c> is the username. AD accepts <c>{0}@example.org</c>.</summary>
    public string BindDnTemplate { get; init; } = "{0}";
    /// <summary>Filter that finds the account after a successful bind. <c>{0}</c> is the username.</summary>
    public string UserFilter { get; init; } = "(&(objectClass=user)(sAMAccountName={0}))";
    public string EmailAttribute { get; init; } = "mail";
    public string DisplayNameAttribute { get; init; } = "displayName";
    /// <summary>Group membership attribute read for role mapping.</summary>
    public string GroupAttribute { get; init; } = "memberOf";
    /// <summary>Substring of a group DN that grants Coordinator, e.g. <c>CN=ResolveDesk-Coordinators</c>.</summary>
    public string? CoordinatorGroup { get; init; }
    public string? AdminGroup { get; init; }
    /// <summary>Create a local user row on first successful directory login.</summary>
    public bool AutoProvisionUsers { get; init; } = true;
}

/// <summary>
/// An external identity provider, in one of two shapes.
///
/// <b>Interactive</b> — set <see cref="ClientId"/>. The API runs the authorization-code flow with
/// PKCE, validates the provider's ID token, and issues its own. This is what lets SSO sit beside a
/// password and a passkey: whichever method someone uses, the session that comes out is the same one.
///
/// <b>Bearer passthrough</b> — leave <see cref="ClientId"/> empty and set Provider to Oidc. The SPA
/// obtains tokens from the provider itself and the API only validates them. Fewer moving parts for a
/// deployment that already has an identity layer in front of it.
///
/// The two are mutually exclusive, because they disagree about whose token the API trusts. Startup
/// says which one is active rather than leaving it to be inferred.
/// </summary>
public sealed record OidcOptions
{
    /// <summary>Issuer URL. Discovery reads <c>{Authority}/.well-known/openid-configuration</c>.</summary>
    public string Authority { get; init; } = "";
    /// <summary>Expected audience when validating a provider-issued token (passthrough shape).</summary>
    public string Audience { get; init; } = "";
    public string RoleClaim { get; init; } = "roles";

    /// <summary>Set this to turn on the interactive flow. Empty means passthrough.</summary>
    public string ClientId { get; init; } = "";
    /// <summary>
    /// Optional. A public client omits it and relies on PKCE alone, which is the right choice when
    /// the secret would have to live somewhere a browser could reach.
    /// </summary>
    public string? ClientSecret { get; init; }
    /// <summary>Must match a redirect URI registered with the provider, exactly.</summary>
    public string RedirectUri { get; init; } = "http://localhost:8080/api/v1/auth/oidc/callback";
    public string Scopes { get; init; } = "openid profile email";

    /// <summary>Where the browser lands once a session exists.</summary>
    public string PostLoginUrl { get; init; } = "http://localhost:5173/auth/callback";

    /// <summary>Value in <see cref="RoleClaim"/> that grants Coordinator; anything else is an Agent.</summary>
    public string? CoordinatorRole { get; init; }
    public string? AdminRole { get; init; }

    /// <summary>Create a local user row the first time someone signs in through the provider.</summary>
    public bool AutoProvisionUsers { get; init; } = true;

    /// <summary>True when the interactive flow is configured.</summary>
    public bool IsInteractive => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Authority);
}

public sealed record AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>False leaves every endpoint open — acceptable for a local demo, never for a deployment.</summary>
    public bool Enabled { get; init; }

    /// <summary>Which backend checks a password. Irrelevant when Password is not among the methods.</summary>
    public AuthProvider Provider { get; init; } = AuthProvider.Local;

    /// <summary>
    /// The sign-in methods this deployment offers, all at once. Defaults to whatever
    /// <see cref="Provider"/> implies, so an existing configuration keeps working untouched.
    /// </summary>
    public AuthMethod Methods { get; init; } = AuthMethod.Password;

    public TotpOptions Totp { get; init; } = new();
    public PasskeyOptions Passkey { get; init; } = new();
    public InvitationOptions Invitations { get; init; } = new();

    public bool Allows(AuthMethod method) => (Methods & method) == method;
    public JwtOptions Jwt { get; init; } = new();
    public LdapOptions Ldap { get; init; } = new();
    public OidcOptions Oidc { get; init; } = new();
    /// <summary>
    /// One-time admin bootstrap. The password is read from the environment only; with no value set no
    /// admin is created, which is why the product ships with no default credentials at all.
    /// </summary>
    public string? BootstrapAdminEmail { get; init; }
    public string? BootstrapAdminPassword { get; init; }

    public static AuthOptions Read(IConfiguration config)
    {
        var s = config.GetSection(SectionName);
        return new AuthOptions
        {
            Enabled = bool.TryParse(s["Enabled"], out var e) && e,
            Provider = Enum.TryParse<AuthProvider>(s["Provider"], ignoreCase: true, out var p) ? p : AuthProvider.Local,
            Methods = ReadMethods(s, p),
            Totp = new TotpOptions
            {
                Enabled = bool.TryParse(s["Totp:Enabled"], out var te) && te,
                Required = bool.TryParse(s["Totp:Required"], out var tr) && tr,
                Issuer = s["Totp:Issuer"] ?? "ResolveDesk",
                WindowSteps = int.TryParse(s["Totp:WindowSteps"], out var tw) ? Math.Clamp(tw, 0, 10) : 1,
            },
            Passkey = new PasskeyOptions
            {
                RelyingPartyId = s["Passkey:RelyingPartyId"] ?? "localhost",
                RelyingPartyName = s["Passkey:RelyingPartyName"] ?? "ResolveDesk",
                Origins = s.GetSection("Passkey:Origins").GetChildren()
                    .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList()
                    is { Count: > 0 } origins ? origins : ["http://localhost:5173"],
                RequireUserVerification = !bool.TryParse(s["Passkey:RequireUserVerification"], out var uv) || uv,
            },
            Invitations = new InvitationOptions
            {
                LifetimeHours = int.TryParse(s["Invitations:LifetimeHours"], out var ih) ? Math.Clamp(ih, 1, 720) : 48,
                BaseUrl = (s["Invitations:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/'),
            },
            Jwt = new JwtOptions
            {
                Issuer = s["Jwt:Issuer"] ?? "resolvedesk",
                Audience = s["Jwt:Audience"] ?? "resolvedesk",
                SigningKey = s["Jwt:SigningKey"],
                LifetimeMinutes = int.TryParse(s["Jwt:LifetimeMinutes"], out var l) ? l : 480,
            },
            Ldap = new LdapOptions
            {
                Host = s["Ldap:Host"] ?? "",
                Port = int.TryParse(s["Ldap:Port"], out var port) ? port : 636,
                UseSsl = !bool.TryParse(s["Ldap:UseSsl"], out var ssl) || ssl,
                BaseDn = s["Ldap:BaseDn"] ?? "",
                BindDnTemplate = s["Ldap:BindDnTemplate"] ?? "{0}",
                UserFilter = s["Ldap:UserFilter"] ?? "(&(objectClass=user)(sAMAccountName={0}))",
                EmailAttribute = s["Ldap:EmailAttribute"] ?? "mail",
                DisplayNameAttribute = s["Ldap:DisplayNameAttribute"] ?? "displayName",
                GroupAttribute = s["Ldap:GroupAttribute"] ?? "memberOf",
                CoordinatorGroup = s["Ldap:CoordinatorGroup"],
                AdminGroup = s["Ldap:AdminGroup"],
                AutoProvisionUsers = !bool.TryParse(s["Ldap:AutoProvisionUsers"], out var ap) || ap,
            },
            Oidc = new OidcOptions
            {
                Authority = (s["Oidc:Authority"] ?? "").TrimEnd('/'),
                Audience = s["Oidc:Audience"] ?? "",
                RoleClaim = s["Oidc:RoleClaim"] ?? "roles",
                ClientId = s["Oidc:ClientId"] ?? "",
                ClientSecret = s["Oidc:ClientSecret"],
                RedirectUri = s["Oidc:RedirectUri"] ?? "http://localhost:8080/api/v1/auth/oidc/callback",
                Scopes = s["Oidc:Scopes"] ?? "openid profile email",
                PostLoginUrl = s["Oidc:PostLoginUrl"] ?? "http://localhost:5173/auth/callback",
                CoordinatorRole = s["Oidc:CoordinatorRole"],
                AdminRole = s["Oidc:AdminRole"],
                AutoProvisionUsers = !bool.TryParse(s["Oidc:AutoProvisionUsers"], out var oap) || oap,
            },
            BootstrapAdminEmail = s["BootstrapAdminEmail"],
            BootstrapAdminPassword = s["BootstrapAdminPassword"],
        };

        /// <summary>
        /// Reads the enabled methods. An older configuration names only a single Provider, so that is
        /// translated rather than ignored: Oidc means SSO, anything else means a password.
        /// </summary>
        static AuthMethod ReadMethods(IConfiguration section, AuthProvider provider)
        {
            var configured = section.GetSection("Methods").GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Aggregate(AuthMethod.None, (acc, v) =>
                    Enum.TryParse<AuthMethod>(v, ignoreCase: true, out var m) ? acc | m : acc);

            if (configured != AuthMethod.None) return configured;
            return provider is AuthProvider.Oidc ? AuthMethod.Oidc : AuthMethod.Password;
        }
    }
}

/// <summary>An identity that passed credential validation, before a token is issued for it.</summary>
public sealed record AuthenticatedUser(long Id, string FullName, string Email, UserRole Role);

/// <summary>
/// Checks a username and password. Implementations differ only in where the check happens — the local
/// users table or a directory — so swapping providers changes one configuration value.
/// </summary>
public interface IIdentityValidator
{
    AuthProvider Provider { get; }
    Task<AuthenticatedUser?> ValidateAsync(string username, string password, CancellationToken ct = default);
}

public interface ITokenIssuer
{
    /// <summary>Signed access token plus the moment it stops being valid.</summary>
    (string Token, DateTime ExpiresAtUtc) Issue(AuthenticatedUser user);
}

/// <summary>Password storage for the Local provider; unused by the directory and OIDC providers.</summary>
public interface ICredentialStore
{
    Task<(long Id, string FullName, string Email, UserRole Role, string? PasswordHash)?> FindByEmailAsync(
        string email, CancellationToken ct = default);
    Task SetPasswordHashAsync(long userId, string hash, CancellationToken ct = default);
    /// <summary>Finds an existing directory user or creates one — used by LDAP auto-provisioning.</summary>
    Task<AuthenticatedUser> UpsertDirectoryUserAsync(
        string email, string fullName, UserRole role, CancellationToken ct = default);
}
