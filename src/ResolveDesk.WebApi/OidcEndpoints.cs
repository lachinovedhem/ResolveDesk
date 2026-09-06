using Microsoft.AspNetCore.Http.HttpResults;
using ResolveDesk.Application;
using ResolveDesk.Infrastructure.Auth;

namespace ResolveDesk.WebApi;

/// <summary>
/// The interactive OpenID Connect flow, run server-side.
///
/// Three endpoints and one redirect each way: <c>/start</c> sends the browser to the provider,
/// <c>/callback</c> receives the code and turns it into a ResolveDesk session, and <c>/exchange</c>
/// hands that session to the SPA. The last one exists so the token never travels in a URL.
/// </summary>
public static class OidcEndpoints
{
    public static void MapOidc(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<AuthOptions>();
        if (!options.Enabled || !options.Allows(AuthMethod.Oidc) || !options.Oidc.IsInteractive) return;

        var group = app.MapGroup("/api/v1/auth/oidc");

        // Entry point. A redirect rather than JSON, so it can be a plain link or a form target and
        // work with the browser's own navigation — no fetch, no CORS, no token in script memory.
        group.MapGet("/start", async Task<Results<RedirectHttpResult, ProblemHttpResult>> (
            OidcService oidc, ILoggerFactory loggers, CancellationToken ct) =>
        {
            try
            {
                return TypedResults.Redirect(await oidc.BuildAuthorizationUrlAsync(ct));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                // Discovery failing is an operator problem — an unreachable authority, a mismatched
                // issuer — so it is worth saying out loud rather than bouncing the user silently.
                loggers.CreateLogger("Auth").LogError(ex, "OIDC discovery failed.");
                return TypedResults.Problem(
                    title: "The identity provider could not be reached.",
                    detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .AllowAnonymous();

        // Where the provider sends the browser back. Everything that can go wrong here ends at the
        // same place with the same message: the SPA's sign-in screen, with a flag in the URL.
        group.MapGet("/callback", async Task<RedirectHttpResult> (
            HttpContext http, OidcService oidc, IHandleStore handles,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            var log = loggers.CreateLogger("Auth");
            var query = http.Request.Query;

            if (query["error"].FirstOrDefault() is { Length: > 0 } error)
            {
                // The person declined consent, or the provider refused the client.
                log.LogInformation("The provider returned an error: {Error}", error);
                return TypedResults.Redirect(Failure(options, "denied"));
            }

            var code = query["code"].FirstOrDefault();
            var state = query["state"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return TypedResults.Redirect(Failure(options, "invalid"));

            AuthenticatedUser? user;
            try
            {
                user = await oidc.CompleteAsync(code, state, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                log.LogError(ex, "The OIDC code exchange failed.");
                return TypedResults.Redirect(Failure(options, "unavailable"));
            }

            if (user is null) return TypedResults.Redirect(Failure(options, "rejected"));

            log.LogInformation("User {UserId} signed in through the identity provider.", user.Id);

            // A one-time code, not the session itself — see SignInHandles.
            var handoff = await SignInHandles.IssueHandoffAsync(handles, user, ct);
            var separator = options.Oidc.PostLoginUrl.Contains('?') ? '&' : '?';
            return TypedResults.Redirect($"{options.Oidc.PostLoginUrl}{separator}code={Uri.EscapeDataString(handoff)}");
        })
        .AllowAnonymous();

        // The SPA trades the code for the session. Rate limited with the other sign-in endpoints: a
        // sixty-second window is short, but it is not a reason to allow unlimited guesses inside it.
        group.MapPost("/exchange", async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> (
            OidcExchangeRequest body, ITokenIssuer issuer,
            IHandleStore handles, CancellationToken ct) =>
        {
            if (await SignInHandles.ConsumeHandoffAsync(handles, body.Code, ct) is not { } user)
                return TypedResults.Unauthorized();

            var (token, expires) = issuer.Issue(user);
            return TypedResults.Ok(new LoginResponse(
                token, expires, user.Id, user.FullName, user.Email, user.Role));
        })
        .AllowAnonymous()
        .RequireRateLimiting("login");
    }

    /// <summary>
    /// One destination for every failure, carrying a reason the SPA can translate. The reasons are
    /// coarse on purpose: "rejected" covers a bad signature, a stale state and an unknown account
    /// alike, because the browser has no business learning which.
    /// </summary>
    private static string Failure(AuthOptions options, string reason)
    {
        var separator = options.Oidc.PostLoginUrl.Contains('?') ? '&' : '?';
        return $"{options.Oidc.PostLoginUrl}{separator}error={reason}";
    }
}
