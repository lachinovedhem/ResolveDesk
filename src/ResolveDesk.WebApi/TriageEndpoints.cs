using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.WebApi;

public static class TriageEndpoints
{
    public static void MapTriage(this WebApplication app, bool requireAuth)
    {
        var t = app.MapGroup("/api/v1/tickets");
        if (requireAuth) t.RequireAuthorization();

        // What the model concluded about this ticket. Absent until the background worker has run,
        // or permanently absent when no chat model is configured — 204 says so without inventing one.
        t.MapGet("/{id:long}/assessment", async Task<Results<Ok<TicketAssessment>, NoContent>> (
            long id, IAssessmentService assessments, CancellationToken ct) =>
            await assessments.GetAsync(id, ct) is { } a ? TypedResults.Ok(a) : TypedResults.NoContent());

        // Re-run it: useful after the description has been corrected, or after switching models.
        t.MapPost("/{id:long}/assessment", async Task<Results<Ok<TicketAssessment>, NoContent>> (
            long id, IAssessmentService assessments, CancellationToken ct) =>
            await assessments.AssessAsync(id, ct) is { } a ? TypedResults.Ok(a) : TypedResults.NoContent());

        // Who should take this, and the numbers behind the answer. The stored analysis is returned
        // when there is one; the first view of a ticket computes it once and keeps it. Re-running is
        // a separate, deliberate action rather than something every page load pays for.
        t.MapGet("/{id:long}/routing", async Task<Results<Ok<RoutingRecommendation>, NotFound>> (
            long id, IRoutingService routing, CancellationToken ct) =>
        {
            try
            {
                return TypedResults.Ok(await routing.GetAsync(id, ct) ?? await routing.RecommendAsync(id, ct));
            }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        });

        // Re-run: after the team changes, the ticket is edited, or more similar tickets are resolved.
        t.MapPost("/{id:long}/routing", async Task<Results<Ok<RoutingRecommendation>, NotFound>> (
            long id, IRoutingService routing, CancellationToken ct) =>
        {
            try { return TypedResults.Ok(await routing.RecommendAsync(id, ct)); }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        });

        t.MapGet("/{id:long}/resolution-review", async Task<Results<Ok<ResolutionReview>, NoContent>> (
            long id, IResolutionReviewService reviews, CancellationToken ct) =>
            await reviews.GetAsync(id, ct) is { } r ? TypedResults.Ok(r) : TypedResults.NoContent());

        var n = app.MapGroup("/api/v1/notifications");
        if (requireAuth) n.RequireAuthorization();

        n.MapGet("/", async Task<Results<Ok<NotificationPage>, UnauthorizedHttpResult>> (
            ClaimsPrincipal principal, INotificationRepository repo, CancellationToken ct,
            bool? unreadOnly, int? limit, long? userId) =>
        {
            if (Recipient(principal, userId) is not { } uid) return TypedResults.Unauthorized();
            var items = await repo.ListAsync(uid, unreadOnly ?? false, limit ?? 50, ct);
            return TypedResults.Ok(new NotificationPage(items, await repo.UnreadCountAsync(uid, ct)));
        });

        n.MapPost("/{id:long}/read", async Task<Results<NoContent, UnauthorizedHttpResult>> (
            long id, ClaimsPrincipal principal, INotificationRepository repo, CancellationToken ct, long? userId) =>
        {
            if (Recipient(principal, userId) is not { } uid) return TypedResults.Unauthorized();
            await repo.MarkReadAsync(uid, id, ct);
            return TypedResults.NoContent();
        });

        n.MapPost("/read-all", async Task<Results<NoContent, UnauthorizedHttpResult>> (
            ClaimsPrincipal principal, INotificationRepository repo, CancellationToken ct, long? userId) =>
        {
            if (Recipient(principal, userId) is not { } uid) return TypedResults.Unauthorized();
            await repo.MarkAllReadAsync(uid, ct);
            return TypedResults.NoContent();
        });

        // Server-sent events: the live half of the notification system. Chosen over WebSockets because
        // the traffic is one-way and SSE reconnects on its own — the browser handles it, we do not.
        n.MapGet("/stream", async (
            HttpContext http, ClaimsPrincipal principal, INotificationHub hub,
            CancellationToken ct, long? userId) =>
        {
            if (Recipient(principal, userId) is not { } uid)
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            // Without this a reverse proxy will happily sit on the stream and buffer it into silence.
            http.Response.Headers["X-Accel-Buffering"] = "no";

            // Tell the browser to wait 5s before reconnecting, and prove the stream is alive right away.
            await http.Response.WriteAsync("retry: 5000\n\n", ct);
            await http.Response.Body.FlushAsync(ct);

            try
            {
                await foreach (var notification in hub.SubscribeAsync(uid, ct))
                {
                    var json = JsonSerializer.Serialize(notification, AppJsonContext.Default.Notification);
                    await http.Response.WriteAsync($"event: notification\ndata: {json}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // The client navigated away or closed the tab. Nothing to report.
            }
        });
    }

    /// <summary>
    /// Whose notifications these are. With authentication on it is always the caller's own — the
    /// query parameter is ignored, so no one can read someone else's bell by guessing an id. With
    /// authentication off (local demo) there is no identity, so the parameter is the only way to say.
    /// </summary>
    private static long? Recipient(ClaimsPrincipal principal, long? fallbackUserId)
    {
        var claim = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        if (long.TryParse(claim, out var authenticated)) return authenticated;
        return principal.Identity?.IsAuthenticated == true ? null : fallbackUserId;
    }
}
