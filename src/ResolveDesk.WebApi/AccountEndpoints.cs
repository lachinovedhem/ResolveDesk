using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.WebApi;

/// <summary>
/// Account lifecycle and the second and third sign-in factors: invitations, TOTP and passkeys.
///
/// There is no self-registration anywhere in this product. An account exists because someone with an
/// account created it, and its first credential arrives over a single-use link rather than as a
/// password somebody else chose and emailed in plain text.
/// </summary>
public static class AccountEndpoints
{
    public static void MapAccounts(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<AuthOptions>();

        MapInvitations(app, options);
        MapTotp(app, options);
        MapPasskeys(app, options);
    }

    private static void MapInvitations(WebApplication app, AuthOptions options)
    {
        var admin = app.MapGroup("/api/v1/invitations");
        if (options.Enabled) admin.RequireAuthorization("coordinator");

        // Creates the account and returns its setup link — once. The token is stored only as a hash,
        // so this response is the single opportunity to copy it.
        admin.MapPost("/", async Task<Results<Ok<InvitationResponse>, BadRequest<ErrorResponse>>> (
            InviteRequest body, ClaimsPrincipal principal, IInvitationService invitations, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.FullName))
                return TypedResults.BadRequest(new ErrorResponse("A full name and an email address are required."));

            try
            {
                var (invitation, url) = await invitations.InviteAsync(
                    new UserCreate(body.FullName, body.Email, body.Role, body.Skills), CallerId(principal), ct);
                return TypedResults.Ok(Describe(invitation, url));
            }
            catch (Exception ex) when (ex.GetType().Name == "PostgresException")
            {
                return TypedResults.BadRequest(new ErrorResponse("That email address already has an account."));
            }
        });

        admin.MapGet("/", async (IInvitationService invitations, CancellationToken ct) =>
            Results.Ok((await invitations.ListPendingAsync(ct)).Select(i => Describe(i, null)).ToList()));

        // Issues a fresh link and invalidates the previous one, for an invitation that has lapsed.
        admin.MapPost("/{userId:long}/resend", async Task<Results<Ok<InvitationResponse>, NotFound>> (
            long userId, ClaimsPrincipal principal, IInvitationService invitations, CancellationToken ct) =>
            await invitations.ResendAsync(userId, CallerId(principal), ct) is var (invitation, url)
                ? TypedResults.Ok(Describe(invitation, url))
                : TypedResults.NotFound());

        admin.MapDelete("/{userId:long}", async Task<Results<NoContent, NotFound>> (
            long userId, IInvitationService invitations, CancellationToken ct) =>
            await invitations.RevokeAsync(userId, ct) ? TypedResults.NoContent() : TypedResults.NotFound());

        // The two anonymous halves of accepting an invitation: look at it, then use it up.
        var public_ = app.MapGroup("/api/v1/invite");

        public_.MapGet("/{token}", async Task<Results<Ok<InvitationPeekResponse>, NotFound>> (
            string token, IInvitationService invitations, CancellationToken ct) =>
            await invitations.PeekAsync(token, ct) is { } invitation
                ? TypedResults.Ok(new InvitationPeekResponse(invitation.FullName, invitation.Email, invitation.ExpiresAtUtc))
                : TypedResults.NotFound())
            .AllowAnonymous();

        public_.MapPost("/{token}", async Task<Results<Ok<LoginResponse>, BadRequest<ErrorResponse>>> (
            string token, AcceptInviteRequest body, IInvitationService invitations,
            ITokenIssuer issuer, CancellationToken ct) =>
        {
            if ((body.Password ?? "").Length < 12)
            {
                return TypedResults.BadRequest(new ErrorResponse(
                    "Choose a password of at least 12 characters."));
            }

            var user = await invitations.AcceptAsync(token, body.Password!, ct);
            if (user is null)
            {
                // Expired, already used, or never existed — all the same answer, so a probe cannot
                // learn which links were once real.
                return TypedResults.BadRequest(new ErrorResponse(
                    "This link is no longer valid. Ask for a new one."));
            }

            var (issued, expires) = issuer.Issue(user);
            return TypedResults.Ok(new LoginResponse(
                issued, expires, user.Id, user.FullName, user.Email, user.Role));
        })
        .AllowAnonymous()
        .RequireRateLimiting("login");
    }

    private static void MapTotp(WebApplication app, AuthOptions options)
    {
        var group = app.MapGroup("/api/v1/account/totp");
        if (options.Enabled) group.RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, ITotpService totp, CancellationToken ct) =>
            Results.Ok(new TotpStatusResponse(
                options.Totp.Enabled, options.Totp.Required,
                await totp.IsEnabledAsync(CallerId(principal), ct))));

        // Returns the secret and its otpauth:// URI. The factor is not active yet — that needs a code.
        group.MapPost("/setup", async Task<Results<Ok<TotpSetupResponse>, BadRequest<ErrorResponse>>> (
            ClaimsPrincipal principal, ITotpService totp, CancellationToken ct) =>
        {
            if (!options.Totp.Enabled)
                return TypedResults.BadRequest(new ErrorResponse("Two-factor authentication is switched off."));

            var (secret, uri) = await totp.BeginEnrolmentAsync(CallerId(principal), ct);
            return TypedResults.Ok(new TotpSetupResponse(secret, uri));
        });

        group.MapPost("/enable", async Task<Results<NoContent, BadRequest<ErrorResponse>>> (
            TotpCodeRequest body, ClaimsPrincipal principal, ITotpService totp, CancellationToken ct) =>
            await totp.ConfirmEnrolmentAsync(CallerId(principal), body.Code ?? "", ct)
                ? TypedResults.NoContent()
                : TypedResults.BadRequest(new ErrorResponse("That code did not match. Check the clock on your phone.")));

        group.MapPost("/disable", async Task<Results<NoContent, BadRequest<ErrorResponse>>> (
            TotpCodeRequest body, ClaimsPrincipal principal, ITotpService totp, CancellationToken ct) =>
            await totp.DisableAsync(CallerId(principal), body.Code ?? "", ct)
                ? TypedResults.NoContent()
                : TypedResults.BadRequest(new ErrorResponse("A current code is required to turn this off.")));
    }

    private static void MapPasskeys(WebApplication app, AuthOptions options)
    {
        var group = app.MapGroup("/api/v1/account/passkeys");
        if (options.Enabled) group.RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, IPasskeyService passkeys, CancellationToken ct) =>
            Results.Ok(await passkeys.ListAsync(CallerId(principal), ct)));

        group.MapDelete("/{id:long}", async Task<Results<NoContent, NotFound>> (
            long id, ClaimsPrincipal principal, IPasskeyService passkeys, CancellationToken ct) =>
            await passkeys.DeleteAsync(CallerId(principal), id, ct)
                ? TypedResults.NoContent() : TypedResults.NotFound());

        // The options blob is passed to navigator.credentials.create() verbatim.
        group.MapPost("/register/begin", async Task<Results<Ok<PasskeyChallengeResponse>, BadRequest<ErrorResponse>>> (
            ClaimsPrincipal principal, IPasskeyService passkeys, CancellationToken ct) =>
        {
            if (!options.Allows(AuthMethod.Passkey))
                return TypedResults.BadRequest(new ErrorResponse("Passkeys are not enabled."));

            var (challengeId, json) = await passkeys.BeginRegistrationAsync(CallerId(principal), ct);
            return TypedResults.Ok(new PasskeyChallengeResponse(challengeId, json));
        });

        group.MapPost("/register/finish", async Task<Results<NoContent, BadRequest<ErrorResponse>>> (
            PasskeyRegisterRequest body, ClaimsPrincipal principal, IPasskeyService passkeys, CancellationToken ct) =>
            await passkeys.FinishRegistrationAsync(
                CallerId(principal), body.ChallengeId ?? "", body.Label ?? "Passkey", body.Credential ?? "", ct)
                ? TypedResults.NoContent()
                : TypedResults.BadRequest(new ErrorResponse("That passkey could not be registered.")));

        // Sign-in with a passkey is anonymous by definition — the credential names the account.
        var login = app.MapGroup("/api/v1/auth/passkey");

        login.MapPost("/begin", async Task<Results<Ok<PasskeyChallengeResponse>, BadRequest<ErrorResponse>>> (
            IPasskeyService passkeys, CancellationToken ct) =>
        {
            if (!options.Allows(AuthMethod.Passkey))
                return TypedResults.BadRequest(new ErrorResponse("Passkeys are not enabled."));

            var (challengeId, json) = await passkeys.BeginAuthenticationAsync(ct);
            return TypedResults.Ok(new PasskeyChallengeResponse(challengeId, json));
        })
        .AllowAnonymous();

        login.MapPost("/finish", async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> (
            PasskeyLoginRequest body, IPasskeyService passkeys, ITokenIssuer issuer, CancellationToken ct) =>
        {
            var user = await passkeys.FinishAuthenticationAsync(body.ChallengeId ?? "", body.Credential ?? "", ct);
            if (user is null) return TypedResults.Unauthorized();

            var (token, expires) = issuer.Issue(user);
            return TypedResults.Ok(new LoginResponse(token, expires, user.Id, user.FullName, user.Email, user.Role));
        })
        .AllowAnonymous()
        .RequireRateLimiting("login");
    }

    private static InvitationResponse Describe(Invitation invitation, string? url) =>
        new(invitation.UserId, invitation.FullName, invitation.Email, invitation.Role,
            invitation.ExpiresAtUtc, invitation.CreatedAtUtc, url);

    /// <summary>
    /// The signed-in account's id. With authentication off there is no principal, so everything falls
    /// to user 0 — acceptable only because that configuration is the local demo, never a deployment.
    /// </summary>
    private static long CallerId(ClaimsPrincipal principal) =>
        long.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"),
            out var id) ? id : 0;
}
