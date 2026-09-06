using System.Text.Json;
using System.Text.Json.Serialization;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.WebApi;

/// <summary>
/// The identity behind a handle, as stored. A record rather than the domain type so the shape written
/// to the database is deliberate and stable — a field added to <see cref="AuthenticatedUser"/> later
/// should not silently change what a live handle deserialises into.
/// </summary>
public sealed record HeldIdentity(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] UserRole Role);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(HeldIdentity))]
internal sealed partial class HandleJsonContext : JsonSerializerContext;

/// <summary>
/// Two places where a sign-in is half-finished and the browser must be handed a receipt rather than
/// the result.
///
/// <b>MFA</b> — a password checked out but the account has a second factor. Sending the account back
/// to the browser and trusting it to return the same one with a code would let anyone who knows a
/// user id skip the password entirely.
///
/// <b>SSO handoff</b> — a session exists but has to cross a redirect. As a query parameter a token
/// lands in proxy logs; as a fragment it lands in browser history. A sixty-second single-use code is
/// worth far less in either place than an eight-hour session.
///
/// Both go through <see cref="IHandleStore"/>, so the second request need not reach the instance that
/// served the first.
/// </summary>
internal static class SignInHandles
{
    /// <summary>Long enough to fetch a phone, short enough that a captured handle goes stale fast.</summary>
    private static readonly TimeSpan MfaLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Only has to survive one redirect the browser is already performing.</summary>
    private static readonly TimeSpan HandoffLifetime = TimeSpan.FromSeconds(60);

    public static Task<string> IssueMfaAsync(IHandleStore store, AuthenticatedUser user, CancellationToken ct = default) =>
        store.IssueAsync(HandlePurpose.Mfa, Serialize(user), MfaLifetime, ct);

    public static Task<AuthenticatedUser?> ConsumeMfaAsync(IHandleStore store, string? handle, CancellationToken ct = default) =>
        ConsumeAsync(store, HandlePurpose.Mfa, handle, ct);

    public static Task<string> IssueHandoffAsync(IHandleStore store, AuthenticatedUser user, CancellationToken ct = default) =>
        store.IssueAsync(HandlePurpose.LoginHandoff, Serialize(user), HandoffLifetime, ct);

    public static Task<AuthenticatedUser?> ConsumeHandoffAsync(IHandleStore store, string? handle, CancellationToken ct = default) =>
        ConsumeAsync(store, HandlePurpose.LoginHandoff, handle, ct);

    private static string Serialize(AuthenticatedUser user) =>
        JsonSerializer.Serialize(
            new HeldIdentity(user.Id, user.FullName, user.Email, user.Role),
            HandleJsonContext.Default.HeldIdentity);

    private static async Task<AuthenticatedUser?> ConsumeAsync(
        IHandleStore store, string purpose, string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) return null;

        var payload = await store.ConsumeAsync(purpose, handle, ct);
        if (payload is null) return null;

        var held = JsonSerializer.Deserialize(payload, HandleJsonContext.Default.HeldIdentity);
        return held is null ? null : new AuthenticatedUser(held.Id, held.FullName, held.Email, held.Role);
    }
}
