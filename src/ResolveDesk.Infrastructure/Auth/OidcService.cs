using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure.Auth;

// ── Wire formats ──────────────────────────────────────────────────────────────────────────────────
// Written out and source-generated rather than taken from the OpenIdConnect packages, for the same
// reason the AI clients are (ADR-002): those packages carry reflection that costs the API its clean
// AOT build, and the three documents involved here are small and stable.

public sealed record OidcDiscovery(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("end_session_endpoint")] string? EndSessionEndpoint);

/// <summary>An authorization in flight: what the callback has to be able to prove.</summary>
public sealed record OidcAuthorization(
    [property: JsonPropertyName("codeVerifier")] string CodeVerifier,
    [property: JsonPropertyName("nonce")] string Nonce);

public sealed record OidcTokenResponse(
    [property: JsonPropertyName("id_token")] string? IdToken,
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OidcDiscovery))]
[JsonSerializable(typeof(OidcAuthorization))]
[JsonSerializable(typeof(OidcTokenResponse))]
internal sealed partial class OidcJsonContext : JsonSerializerContext;

/// <summary>
/// The authorization-code flow with PKCE, run server-side.
///
/// The browser never sees the client secret, the code exchange happens over a back channel, and what
/// comes back to the SPA is a ResolveDesk session — not the provider's token. That last part is the
/// point: every sign-in method converges on one token format, so authorization, roles and expiry work
/// identically whether someone used SSO, a password or a passkey.
///
/// Pending authorizations go through <see cref="IHandleStore"/>, so the callback can land on any
/// instance — which, given the browser leaves for the provider and comes back minutes later, it
/// routinely will.
/// </summary>
public sealed class OidcService(
    IHttpClientFactory clients,
    ICredentialStore credentials,
    IHandleStore handles,
    AuthOptions options,
    ILogger<OidcService> logger)
{
    /// <summary>Long enough for a password manager and an MFA prompt at the provider, no longer.</summary>
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(10);

    private OidcDiscovery? _discovery;
    private DateTime _discoveredAtUtc;

    /// <summary>
    /// Discovery is cached for an hour. Providers rotate signing keys, so it is not cached forever;
    /// they do not rotate endpoints every request, so it is not fetched every time either.
    /// </summary>
    private async Task<OidcDiscovery> DiscoverAsync(CancellationToken ct)
    {
        if (_discovery is not null && DateTime.UtcNow - _discoveredAtUtc < TimeSpan.FromHours(1))
            return _discovery;

        var http = clients.CreateClient("oidc");
        var url = $"{options.Oidc.Authority}/.well-known/openid-configuration";
        var document = await http.GetFromJsonAsync(url, OidcJsonContext.Default.OidcDiscovery, ct)
            ?? throw new InvalidOperationException($"The provider at {url} returned no discovery document.");

        // A provider whose discovery names a different issuer than the one configured is either
        // misconfigured or being impersonated; both are worth refusing loudly.
        if (!string.Equals(document.Issuer.TrimEnd('/'), options.Oidc.Authority.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Discovery reports issuer '{document.Issuer}' but Auth:Oidc:Authority is " +
                $"'{options.Oidc.Authority}'. They must match exactly.");
        }

        _discovery = document;
        _discoveredAtUtc = DateTime.UtcNow;
        return document;
    }

    /// <summary>Builds the URL to send the browser to, and remembers what the callback must prove.</summary>
    public async Task<string> BuildAuthorizationUrlAsync(CancellationToken ct = default)
    {
        var discovery = await DiscoverAsync(ct);

        var nonce = RandomToken();
        var verifier = RandomToken();

        // PKCE S256 is used even when a client secret is configured. The secret proves which client is
        // exchanging the code; PKCE proves it is the same party that started the flow.
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        // The state *is* the handle: opaque, single-use and expiring, which is exactly what the
        // parameter is for.
        var state = await handles.IssueAsync(
            HandlePurpose.OidcState,
            JsonSerializer.Serialize(
                new OidcAuthorization(verifier, nonce), OidcJsonContext.Default.OidcAuthorization),
            AuthorizationLifetime, ct);

        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = options.Oidc.ClientId,
            ["redirect_uri"] = options.Oidc.RedirectUri,
            ["scope"] = options.Oidc.Scopes,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };

        var encoded = string.Join("&", query
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

        return $"{discovery.AuthorizationEndpoint}?{encoded}";
    }

    /// <summary>
    /// Exchanges the code, validates the ID token, and maps the claims onto a local account. Returns
    /// null for anything that fails a check — the caller turns that into one generic answer, because
    /// the difference between "unknown state" and "bad signature" is not the browser's business.
    /// </summary>
    public async Task<AuthenticatedUser?> CompleteAsync(string code, string state, CancellationToken ct = default)
    {
        // Single-use: a state that has been presented is gone, valid or not, so a replayed callback
        // finds nothing.
        var stored = await handles.ConsumeAsync(HandlePurpose.OidcState, state, ct);
        if (stored is null) return null;

        var pending = JsonSerializer.Deserialize(stored, OidcJsonContext.Default.OidcAuthorization);
        if (pending is null) return null;

        var discovery = await DiscoverAsync(ct);
        var http = clients.CreateClient("oidc");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = options.Oidc.RedirectUri,
            ["client_id"] = options.Oidc.ClientId,
            ["code_verifier"] = pending.CodeVerifier,
        };
        if (!string.IsNullOrWhiteSpace(options.Oidc.ClientSecret))
            form["client_secret"] = options.Oidc.ClientSecret!;

        using var response = await http.PostAsync(
            discovery.TokenEndpoint, new FormUrlEncodedContent(form), ct);

        var tokens = await response.Content.ReadFromJsonAsync(
            OidcJsonContext.Default.OidcTokenResponse, ct);

        if (tokens?.Error is { Length: > 0 })
        {
            logger.LogWarning("Token exchange refused: {Error} {Description}",
                tokens.Error, tokens.ErrorDescription);
            return null;
        }
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(tokens?.IdToken))
        {
            logger.LogWarning("Token exchange failed with {Status}.", (int)response.StatusCode);
            return null;
        }

        var principal = await ValidateIdTokenAsync(tokens.IdToken!, discovery, pending.Nonce, http, ct);
        if (principal is null) return null;

        return await MapAsync(principal, ct);
    }

    private async Task<JsonWebToken?> ValidateIdTokenAsync(
        string idToken, OidcDiscovery discovery, string expectedNonce, HttpClient http, CancellationToken ct)
    {
        var jwks = await http.GetStringAsync(discovery.JwksUri, ct);

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = discovery.Issuer,
            ValidAudience = options.Oidc.ClientId,
            IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid)
        {
            logger.LogWarning(result.Exception, "The provider's ID token failed validation.");
            return null;
        }

        var token = (JsonWebToken)result.SecurityToken;

        // The nonce ties this token to the authorization *this* browser started. Without the check a
        // token captured from another session would be accepted here.
        if (!token.TryGetClaim("nonce", out var nonce) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(nonce.Value), Encoding.UTF8.GetBytes(expectedNonce)))
        {
            logger.LogWarning("The provider's ID token carried the wrong nonce.");
            return null;
        }

        return token;
    }

    /// <summary>
    /// Turns provider claims into a local account. Email is the join key, because it is the one claim
    /// every provider emits and the one an operator can reconcile by hand when something goes wrong.
    /// </summary>
    private async Task<AuthenticatedUser?> MapAsync(JsonWebToken token, CancellationToken ct)
    {
        var email = Claim(token, "email") ?? Claim(token, "preferred_username") ?? Claim(token, "upn");
        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(
                "The provider returned no email claim; add 'email' to Auth:Oidc:Scopes or map it at the provider.");
            return null;
        }

        var name = Claim(token, "name") ?? Claim(token, "given_name") ?? email;
        var role = MapRole(token);

        if (!options.Oidc.AutoProvisionUsers)
        {
            var existing = await credentials.FindByEmailAsync(email, ct);
            if (existing is null)
            {
                logger.LogWarning(
                    "{Email} authenticated with the provider but has no account here, and " +
                    "Auth:Oidc:AutoProvisionUsers is false.", email);
                return null;
            }
            // The local row wins on role: an operator who set it here meant it.
            return new AuthenticatedUser(existing.Value.Id, existing.Value.FullName, existing.Value.Email, existing.Value.Role);
        }

        return await credentials.UpsertDirectoryUserAsync(email, name, role, ct);
    }

    private UserRole MapRole(JsonWebToken token)
    {
        var claimed = token.Claims
            .Where(c => c.Type == options.Oidc.RoleClaim)
            .Select(c => c.Value)
            .ToList();

        if (Matches(options.Oidc.AdminRole)) return UserRole.Admin;
        if (Matches(options.Oidc.CoordinatorRole)) return UserRole.Coordinator;
        return UserRole.Agent;

        bool Matches(string? configured) =>
            !string.IsNullOrWhiteSpace(configured) &&
            claimed.Any(v => v.Equals(configured, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Claim(JsonWebToken token, string type) =>
        token.TryGetClaim(type, out var claim) && !string.IsNullOrWhiteSpace(claim.Value) ? claim.Value : null;

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
