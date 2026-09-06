using System.Security.Claims;
using ResolveDesk.Application;
using ResolveDesk.Core;
using ResolveDesk.Infrastructure.Auth;

namespace ResolveDesk.WebApi;

public static class AuthEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<AuthOptions>();
        var group = app.MapGroup("/api/v1/auth");

        // Always anonymous: the SPA reads this before it can know whether to show a password form, an
        // "use your corporate account" button, or nothing at all.
        group.MapGet("/config", () =>
        {
            var methods = new List<string>();
            if (options.Allows(AuthMethod.Password)) methods.Add("Password");
            if (options.Allows(AuthMethod.Oidc)) methods.Add("Oidc");
            if (options.Allows(AuthMethod.Passkey)) methods.Add("Passkey");

            return Results.Ok(new AuthConfigResponse(
                options.Enabled,
                options.Provider.ToString(),
                options.Allows(AuthMethod.Oidc) ? options.Oidc.Authority : null,
                options.Allows(AuthMethod.Oidc) ? options.Oidc.Audience : null,
                options.Allows(AuthMethod.Password) && options.Provider is AuthProvider.Local or AuthProvider.Ldap,
                methods,
                options.Allows(AuthMethod.Passkey),
                options.Totp.Enabled,
                options.Totp.Required,
                options.Oidc.IsInteractive));
        })
        .AllowAnonymous();

        if (!options.Enabled) return;

        group.MapPost("/login", async (
            LoginRequest body, IIdentityValidator validator, ITokenIssuer issuer,
            ITotpService totp, IHandleStore handles, ILoggerFactory loggers, CancellationToken ct) =>
        {
            var log = loggers.CreateLogger("Auth");
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrEmpty(body.Password))
                return Results.BadRequest(new ErrorResponse("Username and password are required."));

            AuthenticatedUser? user;
            try
            {
                user = await validator.ValidateAsync(body.Username, body.Password, ct);
            }
            catch (InvalidOperationException ex)
            {
                // Misconfiguration (e.g. OIDC selected, or LDAP host missing) — a 400 with the reason,
                // because it tells an operator what to fix and reveals nothing about any account.
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }

            if (user is null)
            {
                log.LogInformation("Login failed for a submitted username.");
                // One message for every failure mode: no account enumeration. The JsonTypeInfo overload
                // keeps this AOT-safe — the reflection-based one warns at build time.
                return Results.Json(new ErrorResponse("Invalid username or password."),
                    AppJsonContext.Default.ErrorResponse, statusCode: 401);
            }

            // A correct password on an account with a second factor is not yet a session. The short
            // MFA token stands in for "this password was right", and only /auth/mfa turns it into
            // an access token — so a stolen password alone gets no further than this response.
            if (await totp.IsEnabledAsync(user.Id, ct))
            {
                return Results.Ok(new MfaChallengeResponse(
                    true, await SignInHandles.IssueMfaAsync(handles, user, ct)));
            }

            if (options.Totp.Required)
            {
                return Results.Json(
                    new ErrorResponse("This deployment requires two-factor authentication. Ask an administrator to enrol you."),
                    AppJsonContext.Default.ErrorResponse, statusCode: 403);
            }

            var (token, expires) = issuer.Issue(user);
            log.LogInformation("User {UserId} signed in via {Provider}.", user.Id, options.Provider);
            return Results.Ok(new LoginResponse(token, expires, user.Id, user.FullName, user.Email, user.Role));
        })
        .AllowAnonymous()
        .RequireRateLimiting("login");

        // Second leg of a password sign-in. Wrong codes are rate limited alongside passwords, because
        // six digits is a small enough space to be worth guessing at speed.
        group.MapPost("/mfa", async (
            MfaVerifyRequest body, ITotpService totp, ITokenIssuer issuer,
            IHandleStore handles, CancellationToken ct) =>
        {
            if (await SignInHandles.ConsumeMfaAsync(handles, body.MfaToken, ct) is not { } pending)
            {
                return Results.Json(new ErrorResponse("That sign-in attempt has expired. Start again."),
                    AppJsonContext.Default.ErrorResponse, statusCode: 401);
            }

            if (!await totp.VerifyAsync(pending.Id, body.Code ?? "", ct))
            {
                return Results.Json(new ErrorResponse("That code is not valid."),
                    AppJsonContext.Default.ErrorResponse, statusCode: 401);
            }

            var (token, expires) = issuer.Issue(pending);
            return Results.Ok(new LoginResponse(
                token, expires, pending.Id, pending.FullName, pending.Email, pending.Role));
        })
        .AllowAnonymous()
        .RequireRateLimiting("login");

        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? principal.FindFirstValue("sub");
            return Results.Ok(new MeResponse(
                long.TryParse(id, out var parsed) ? parsed : 0,
                principal.Identity?.Name ?? principal.FindFirstValue("name") ?? "",
                principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? "",
                principal.FindFirstValue(ClaimTypes.Role) ?? nameof(UserRole.Agent)));
        })
        .RequireAuthorization();

        // Local provider only: a directory or OIDC account's password lives with its own identity system.
        if (options.Provider is AuthProvider.Local)
        {
            group.MapPost("/password", async (
                ChangePasswordRequest body, ClaimsPrincipal principal,
                ICredentialStore store, CancellationToken ct) =>
            {
                if (body.NewPassword.Length < 12)
                    return Results.BadRequest(new ErrorResponse("New password must be at least 12 characters."));

                var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
                if (!long.TryParse(idClaim, out var userId)) return Results.Unauthorized();

                var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email");
                var current = email is null ? null : await store.FindByEmailAsync(email, ct);
                if (current is null || !PasswordHasher.Verify(body.CurrentPassword, current.Value.PasswordHash))
                    return Results.Json(new ErrorResponse("Current password is incorrect."),
                        AppJsonContext.Default.ErrorResponse, statusCode: 401);

                await store.SetPasswordHashAsync(userId, PasswordHasher.Hash(body.NewPassword), ct);
                return Results.NoContent();
            })
            .RequireAuthorization();
        }
    }
}
