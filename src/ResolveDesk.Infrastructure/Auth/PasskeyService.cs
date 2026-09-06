using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>
/// WebAuthn sign-in: Face ID, Touch ID, Windows Hello, or a hardware key. The private key never
/// leaves the device and the signature is bound to this origin, so a passkey cannot be phished onto
/// a lookalike site the way a password can.
///
/// Challenges go through <see cref="IHandleStore"/>, so the ceremony survives the second request
/// landing on a different instance than the first.
/// </summary>
public sealed class PasskeyService(
    IAuthStore store,
    IHandleStore handles,
    AuthOptions options,
    ILogger<PasskeyService> logger) : IPasskeyService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    public Task<IReadOnlyList<PasskeySummary>> ListAsync(long userId, CancellationToken ct = default) =>
        store.ListPasskeysAsync(userId, ct);

    public Task<bool> DeleteAsync(long userId, long passkeyId, CancellationToken ct = default) =>
        store.DeletePasskeyAsync(userId, passkeyId, ct);

    public async Task<(string ChallengeId, string OptionsJson)> BeginRegistrationAsync(
        long userId, CancellationToken ct = default)
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var id = await IssueAsync(challenge, userId, ct);

        var json = $$"""
            {
              "challenge": "{{WebAuthn.ToBase64Url(challenge)}}",
              "rp": { "id": "{{options.Passkey.RelyingPartyId}}", "name": "{{Escape(options.Passkey.RelyingPartyName)}}" },
              "user": {
                "id": "{{WebAuthn.ToBase64Url(BitConverter.GetBytes(userId))}}",
                "name": "user-{{userId}}",
                "displayName": "user-{{userId}}"
              },
              "pubKeyCredParams": [ { "type": "public-key", "alg": -7 }, { "type": "public-key", "alg": -257 } ],
              "timeout": 120000,
              "attestation": "none",
              "authenticatorSelection": {
                "residentKey": "preferred",
                "userVerification": "{{(options.Passkey.RequireUserVerification ? "required" : "preferred")}}"
              }
            }
            """;
        return (id, json);
    }

    public async Task<bool> FinishRegistrationAsync(
        long userId, string challengeId, string label, string credentialJson, CancellationToken ct = default)
    {
        var pending = await ConsumeAsync(challengeId, ct);
        if (pending is null || pending.UserId != userId) return false;

        try
        {
            var credential = JsonSerializer.Deserialize(credentialJson, WebAuthnJsonContext.Default.RegistrationCredential);
            if (credential is null) return false;

            var clientDataBytes = WebAuthn.FromBase64Url(credential.Response.ClientDataJson);
            if (!CheckClientData(clientDataBytes, pending.Challenge, "webauthn.create")) return false;

            var (credentialId, publicKey, signCount) = WebAuthn.ReadRegistration(
                WebAuthn.FromBase64Url(credential.Response.AttestationObject),
                options.Passkey.RelyingPartyId,
                options.Passkey.RequireUserVerification);

            var name = string.IsNullOrWhiteSpace(label) ? "Passkey" : label.Trim();
            await store.AddPasskeyAsync(userId, credentialId, publicKey, signCount, name[..Math.Min(name.Length, 60)], ct);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            // A malformed or mismatched credential is a failed registration, not a server fault.
            logger.LogWarning(ex, "Passkey registration rejected for user {UserId}.", userId);
            return false;
        }
    }

    public async Task<(string ChallengeId, string OptionsJson)> BeginAuthenticationAsync(CancellationToken ct = default)
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var id = await IssueAsync(challenge, userId: null, ct);

        // No allowCredentials list: the authenticator offers whatever resident key matches this site,
        // which is what lets someone sign in without typing a username first.
        var json = $$"""
            {
              "challenge": "{{WebAuthn.ToBase64Url(challenge)}}",
              "rpId": "{{options.Passkey.RelyingPartyId}}",
              "timeout": 120000,
              "userVerification": "{{(options.Passkey.RequireUserVerification ? "required" : "preferred")}}"
            }
            """;
        return (id, json);
    }

    public async Task<AuthenticatedUser?> FinishAuthenticationAsync(
        string challengeId, string credentialJson, CancellationToken ct = default)
    {
        var pending = await ConsumeAsync(challengeId, ct);
        if (pending is null) return null;

        try
        {
            var credential = JsonSerializer.Deserialize(credentialJson, WebAuthnJsonContext.Default.AssertionCredential);
            if (credential is null) return null;

            var clientDataBytes = WebAuthn.FromBase64Url(credential.Response.ClientDataJson);
            if (!CheckClientData(clientDataBytes, pending.Challenge, "webauthn.get")) return null;

            var stored = await store.FindPasskeyAsync(WebAuthn.FromBase64Url(credential.RawId), ct);
            if (stored is null) return null;

            var verified = WebAuthn.VerifyAssertion(
                stored.PublicKey,
                WebAuthn.FromBase64Url(credential.Response.AuthenticatorData),
                clientDataBytes,
                WebAuthn.FromBase64Url(credential.Response.Signature),
                options.Passkey.RelyingPartyId,
                options.Passkey.RequireUserVerification,
                out var signCount);

            if (!verified) return null;

            // A counter that fails to advance is the documented signal of a cloned authenticator. Some
            // platform authenticators legitimately report zero always, so zero is exempt.
            if (signCount != 0 && signCount <= stored.SignCount)
            {
                logger.LogWarning(
                    "Passkey {PasskeyId} presented a non-increasing signature counter ({Received} <= {Stored}); " +
                    "the credential may have been cloned.", stored.Id, signCount, stored.SignCount);
                return null;
            }

            await store.TouchPasskeyAsync(stored.Id, signCount, ct);
            return new AuthenticatedUser(stored.UserId, stored.FullName, stored.Email, stored.Role);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException)
        {
            logger.LogWarning(ex, "Passkey assertion rejected.");
            return null;
        }
    }

    /// <summary>
    /// The three checks that make a signature mean something: it answers the challenge we issued, it
    /// was produced for the ceremony we asked for, and the browser was on an origin we recognise.
    /// </summary>
    private bool CheckClientData(byte[] clientDataJson, byte[] expectedChallenge, string expectedType)
    {
        var data = JsonSerializer.Deserialize(clientDataJson, WebAuthnJsonContext.Default.ClientData);
        if (data is null || data.Type != expectedType) return false;

        if (!CryptographicOperations.FixedTimeEquals(
                WebAuthn.FromBase64Url(data.Challenge), expectedChallenge)) return false;

        return options.Passkey.Origins.Any(o =>
            string.Equals(o.TrimEnd('/'), data.Origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }

    private Task<string> IssueAsync(byte[] challenge, long? userId, CancellationToken ct) =>
        handles.IssueAsync(
            HandlePurpose.PasskeyChallenge,
            JsonSerializer.Serialize(
                new PendingChallenge(WebAuthn.ToBase64Url(challenge), userId),
                WebAuthnJsonContext.Default.PendingChallenge),
            ChallengeLifetime, ct);

    /// <summary>Single-use: a challenge that has been answered is gone, so a replay finds nothing.</summary>
    private async Task<StoredChallenge?> ConsumeAsync(string challengeId, CancellationToken ct)
    {
        var payload = await handles.ConsumeAsync(HandlePurpose.PasskeyChallenge, challengeId, ct);
        if (payload is null) return null;

        var pending = JsonSerializer.Deserialize(payload, WebAuthnJsonContext.Default.PendingChallenge);
        return pending is null
            ? null
            : new StoredChallenge(WebAuthn.FromBase64Url(pending.Challenge), pending.UserId);
    }

    private sealed record StoredChallenge(byte[] Challenge, long? UserId);

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
