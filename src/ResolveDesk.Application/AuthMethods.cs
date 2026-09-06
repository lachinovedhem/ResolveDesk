using ResolveDesk.Core;

namespace ResolveDesk.Application;

/// <summary>
/// A way of proving who you are. These are not alternatives to choose between at deployment time —
/// a deployment turns on as many as it wants, and the sign-in screen offers all of them. A team can
/// run corporate SSO for staff, a password for the on-call contractor, and passkeys for whoever has
/// set one up, at the same time.
/// </summary>
[Flags]
public enum AuthMethod
{
    None = 0,
    /// <summary>A password, checked against whichever backend <see cref="AuthOptions.Provider"/> names.</summary>
    Password = 1,
    /// <summary>An external OpenID Connect provider. The SPA runs the flow; this API validates the token.</summary>
    Oidc = 2,
    /// <summary>WebAuthn: a platform authenticator (Face ID, Windows Hello, Touch ID) or a security key.</summary>
    Passkey = 4,
}

/// <summary>Second factor, required after a password when the account has one enrolled.</summary>
public sealed record TotpOptions
{
    /// <summary>Offer TOTP enrolment at all. Off means no account can add one.</summary>
    public bool Enabled { get; init; }
    /// <summary>Refuse to sign anyone in until they have enrolled a second factor.</summary>
    public bool Required { get; init; }
    /// <summary>Shown in the authenticator app next to the account name.</summary>
    public string Issuer { get; init; } = "ResolveDesk";
    /// <summary>
    /// How many 30-second steps either side of now are accepted. One step tolerates a phone whose
    /// clock has drifted; more than that widens the window a stolen code stays usable in.
    /// </summary>
    public int WindowSteps { get; init; } = 1;
}

/// <summary>
/// WebAuthn. <see cref="RelyingPartyId"/> must be the site's registrable domain and
/// <see cref="Origins"/> the exact origins the browser will report — a passkey registered for one
/// origin is deliberately useless at another, which is what makes it unphishable.
/// </summary>
public sealed record PasskeyOptions
{
    public string RelyingPartyId { get; init; } = "localhost";
    public string RelyingPartyName { get; init; } = "ResolveDesk";
    public IReadOnlyList<string> Origins { get; init; } = ["http://localhost:5173"];
    /// <summary>Ask the authenticator to verify the human — a PIN, a fingerprint, a face.</summary>
    public bool RequireUserVerification { get; init; } = true;
}

/// <summary>How a new account is handed its first credential.</summary>
public sealed record InvitationOptions
{
    /// <summary>Hours a setup link stays usable. It is single-use regardless.</summary>
    public int LifetimeHours { get; init; } = 48;
    /// <summary>
    /// Where the link points. The API never sends mail itself — it returns the URL, and whoever
    /// created the account passes it on. That keeps an SMTP dependency out of the product.
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:5173";
}

/// <summary>A pending account setup. The token itself is never stored — only its hash.</summary>
public sealed record Invitation(
    long Id,
    long UserId,
    string Email,
    string FullName,
    UserRole Role,
    DateTime ExpiresAtUtc,
    DateTime? ConsumedAtUtc,
    DateTime CreatedAtUtc);

/// <summary>A registered WebAuthn credential, as shown to the person who owns it.</summary>
public sealed record PasskeySummary(
    long Id,
    string Label,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc);

public interface IInvitationService
{
    /// <summary>
    /// Creates the account and a single-use setup link for it. Returns the URL exactly once — the
    /// token is hashed on the way into the database, so it cannot be shown again or recovered.
    /// </summary>
    Task<(Invitation Invitation, string Url)> InviteAsync(
        UserCreate user, long createdBy, CancellationToken ct = default);

    /// <summary>Re-issues a link for an account whose invitation lapsed, invalidating the old one.</summary>
    Task<(Invitation Invitation, string Url)?> ResendAsync(long userId, long createdBy, CancellationToken ct = default);

    /// <summary>Who a token belongs to, or null when it is unknown, expired or already used.</summary>
    Task<Invitation?> PeekAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Sets the password and burns the token. Returns null if the token is no longer valid — which is
    /// what makes a link that has already been opened useless to whoever finds it next.
    /// </summary>
    Task<AuthenticatedUser?> AcceptAsync(string token, string password, CancellationToken ct = default);

    Task<IReadOnlyList<Invitation>> ListPendingAsync(CancellationToken ct = default);
    Task<bool> RevokeAsync(long userId, CancellationToken ct = default);
}

public interface ITotpService
{
    /// <summary>Generates a secret and the otpauth:// URI an authenticator app scans. Not yet active.</summary>
    Task<(string Secret, string OtpAuthUri)> BeginEnrolmentAsync(long userId, CancellationToken ct = default);

    /// <summary>Confirms the person can produce a valid code, and only then turns the factor on.</summary>
    Task<bool> ConfirmEnrolmentAsync(long userId, string code, CancellationToken ct = default);

    Task<bool> VerifyAsync(long userId, string code, CancellationToken ct = default);
    Task<bool> IsEnabledAsync(long userId, CancellationToken ct = default);

    /// <summary>Turning it off requires a current code, so a hijacked session cannot strip the factor.</summary>
    Task<bool> DisableAsync(long userId, string code, CancellationToken ct = default);
}

public interface IPasskeyService
{
    Task<IReadOnlyList<PasskeySummary>> ListAsync(long userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(long userId, long passkeyId, CancellationToken ct = default);

    /// <summary>Options for <c>navigator.credentials.create()</c>, as JSON, plus the challenge id.</summary>
    Task<(string ChallengeId, string OptionsJson)> BeginRegistrationAsync(long userId, CancellationToken ct = default);
    Task<bool> FinishRegistrationAsync(long userId, string challengeId, string label, string credentialJson, CancellationToken ct = default);

    /// <summary>Options for <c>navigator.credentials.get()</c>. No user id: the credential names itself.</summary>
    Task<(string ChallengeId, string OptionsJson)> BeginAuthenticationAsync(CancellationToken ct = default);
    Task<AuthenticatedUser?> FinishAuthenticationAsync(string challengeId, string credentialJson, CancellationToken ct = default);
}

/// <summary>
/// Storage for the credentials the built-in methods own. Directory and OIDC accounts keep their
/// secrets in their own identity system and appear here only as a row with no password.
/// </summary>
public interface IAuthStore
{
    Task<long> CreateInvitedUserAsync(UserCreate user, CancellationToken ct = default);
    Task<Invitation?> FindInvitationByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<long> SaveInvitationAsync(long userId, string tokenHash, DateTime expiresAtUtc, long createdBy, CancellationToken ct = default);
    Task ConsumeInvitationAsync(long invitationId, CancellationToken ct = default);
    Task<bool> RevokeInvitationsAsync(long userId, CancellationToken ct = default);
    Task<IReadOnlyList<Invitation>> ListPendingInvitationsAsync(CancellationToken ct = default);

    Task<string?> GetTotpSecretAsync(long userId, CancellationToken ct = default);
    Task SetTotpSecretAsync(long userId, string? secret, bool enabled, CancellationToken ct = default);
    Task<bool> IsTotpEnabledAsync(long userId, CancellationToken ct = default);

    Task<IReadOnlyList<PasskeySummary>> ListPasskeysAsync(long userId, CancellationToken ct = default);
    Task AddPasskeyAsync(long userId, byte[] credentialId, byte[] publicKey, long signCount, string label, CancellationToken ct = default);
    Task<StoredPasskey?> FindPasskeyAsync(byte[] credentialId, CancellationToken ct = default);
    Task TouchPasskeyAsync(long passkeyId, long signCount, CancellationToken ct = default);
    Task<bool> DeletePasskeyAsync(long userId, long passkeyId, CancellationToken ct = default);
}

/// <summary>A passkey as stored, with the account it authenticates.</summary>
public sealed record StoredPasskey(
    long Id, long UserId, byte[] PublicKey, long SignCount,
    string FullName, string Email, UserRole Role);
