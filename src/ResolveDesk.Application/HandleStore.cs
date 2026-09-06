namespace ResolveDesk.Application;

/// <summary>
/// What a handle is for. Carried alongside the handle so one issued for a passkey challenge cannot be
/// redeemed as a second-factor handle: the purpose is part of the lookup, not a convention.
/// </summary>
public static class HandlePurpose
{
    /// <summary>A password checked out, but the account has a second factor.</summary>
    public const string Mfa = "mfa";
    /// <summary>A session waiting to cross the redirect back from an identity provider.</summary>
    public const string LoginHandoff = "login-handoff";
    /// <summary>A WebAuthn challenge, registration or assertion.</summary>
    public const string PasskeyChallenge = "passkey-challenge";
    /// <summary>An OIDC authorization in flight: the PKCE verifier and the nonce.</summary>
    public const string OidcState = "oidc-state";
}

/// <summary>
/// Short-lived, single-use handles for something the server holds on a browser's behalf.
///
/// Four flows need this and they need it for the same reason: a step has completed, but handing the
/// browser the *result* would be worse than handing it a receipt. The value stays server-side and the
/// browser carries an opaque, expiring, one-shot reference.
///
/// Shared rather than per-process, because every one of these spans two requests, and behind a load
/// balancer the second request routinely lands on a different instance than the first. In memory that
/// looked fine in development and would have failed in production roughly half the time.
///
/// Implementations must make <see cref="ConsumeAsync"/> atomic: two simultaneous redemptions of one
/// handle must not both succeed, or a captured handle becomes usable in parallel with its owner.
/// </summary>
public interface IHandleStore
{
    /// <summary>Stores <paramref name="payload"/> and returns the handle that redeems it, once.</summary>
    Task<string> IssueAsync(string purpose, string payload, TimeSpan lifetime, CancellationToken ct = default);

    /// <summary>
    /// Returns the payload and destroys the handle, or null if it is unknown, expired, already used,
    /// or was issued for a different purpose. The handle is spent on the first attempt whether or not
    /// what follows succeeds — so a wrong second-factor code costs a full re-authentication rather
    /// than buying unlimited guesses against one password check.
    /// </summary>
    Task<string?> ConsumeAsync(string purpose, string handle, CancellationToken ct = default);
}
