using System.Security.Cryptography;
using System.Text;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>
/// Accounts are created by someone who already has one — there is no self-registration — and the new
/// person receives a single-use link instead of a password chosen for them.
///
/// The token is 256 bits of cryptographic randomness. Only its SHA-256 hash reaches the database, so
/// a leaked dump yields no working links, and the URL is returned exactly once at creation: the
/// product cannot show it again because it genuinely no longer knows it. Opening the link burns it,
/// which is what stops a forwarded email being a permanent way in.
/// </summary>
public sealed class InvitationService(
    IAuthStore store,
    ICredentialStore credentials,
    AuthOptions options) : IInvitationService
{
    public async Task<(Invitation Invitation, string Url)> InviteAsync(
        UserCreate user, long createdBy, CancellationToken ct = default)
    {
        var userId = await store.CreateInvitedUserAsync(user, ct);
        return await IssueAsync(userId, createdBy, ct)
            ?? throw new InvalidOperationException("The account was created but its invitation could not be stored.");
    }

    public Task<(Invitation Invitation, string Url)?> ResendAsync(
        long userId, long createdBy, CancellationToken ct = default) =>
        IssueAsync(userId, createdBy, ct);

    private async Task<(Invitation Invitation, string Url)?> IssueAsync(
        long userId, long createdBy, CancellationToken ct)
    {
        var token = Token();
        var expires = DateTime.UtcNow.AddHours(options.Invitations.LifetimeHours);
        var id = await store.SaveInvitationAsync(userId, Hash(token), expires, createdBy, ct);

        // Read it back rather than assembling it here, so the record always reflects what was stored.
        var invitation = (await store.ListPendingInvitationsAsync(ct)).FirstOrDefault(i => i.Id == id);
        return invitation is null ? null : (invitation, $"{options.Invitations.BaseUrl}/invite/{token}");
    }

    public Task<Invitation?> PeekAsync(string token, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<Invitation?>(null)
            : store.FindInvitationByHashAsync(Hash(token), ct);

    public async Task<AuthenticatedUser?> AcceptAsync(string token, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(password)) return null;

        var invitation = await store.FindInvitationByHashAsync(Hash(token), ct);
        if (invitation is null) return null;

        await credentials.SetPasswordHashAsync(invitation.UserId, PasswordHasher.Hash(password), ct);
        await store.ConsumeInvitationAsync(invitation.Id, ct);

        return new AuthenticatedUser(
            invitation.UserId, invitation.FullName, invitation.Email, invitation.Role);
    }

    public Task<IReadOnlyList<Invitation>> ListPendingAsync(CancellationToken ct = default) =>
        store.ListPendingInvitationsAsync(ct);

    public Task<bool> RevokeAsync(long userId, CancellationToken ct = default) =>
        store.RevokeInvitationsAsync(userId, ct);

    /// <summary>256 bits, URL-safe. Long enough that guessing is not a threat model worth modelling.</summary>
    private static string Token() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
