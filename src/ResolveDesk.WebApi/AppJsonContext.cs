using System.Text.Json.Serialization;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.WebApi;

// Request bodies for endpoints that aren't plain domain DTOs.
public sealed record AssignRequest(long AssigneeId, long CoordinatorId);
/// <param name="SkipTriage">
/// Set when importing history rather than closing live work. Resolving a ticket normally queues an AI
/// review of the resolution; importing a back catalogue would queue one per record and swamp the model
/// for output nobody asked for. The ticket and its resolution are stored either way, so the archive is
/// still fully searchable — only the review is skipped.
/// </param>
public sealed record StatusRequest(TicketStatus Status, string? Resolution, bool? SkipTriage = null);
public sealed record CommentRequest(long? AuthorId, string Body);
public sealed record StatsResponse(long Open, long Assigned, long InProgress, long WaitingCustomer, long Resolved, long Closed);
public sealed record IdResponse(long Id);

/// <summary>Ad-hoc knowledge lookup — lets an agent search past resolutions before a ticket exists.</summary>
public sealed record KnowledgeSearchRequest(string Title, string? Description, int? Limit);

// Authentication contracts.
public sealed record LoginRequest(string Username, string Password);
public sealed record LoginResponse(
    string Token, DateTime ExpiresAtUtc, long UserId, string FullName, string Email, UserRole Role);
public sealed record MeResponse(long UserId, string FullName, string Email, string Role);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
/// <summary>
/// What the SPA needs before login. Methods are plural on purpose: a deployment can offer a password,
/// corporate SSO and passkeys at the same time, and the sign-in screen renders whatever is on.
/// </summary>
/// <param name="OidcInteractive">
/// True when this API runs the sign-in flow itself, so the SPA can simply send the browser to
/// /auth/oidc/start. False means the deployment expects the SPA to obtain a provider token on its
/// own — a button that redirected into a route that does not exist would be worse than no button.
/// </param>
public sealed record AuthConfigResponse(
    bool Enabled, string Provider, string? Authority, string? Audience, bool AcceptsPassword,
    IReadOnlyList<string> Methods, bool PasskeysEnabled, bool TotpAvailable, bool TotpRequired,
    bool OidcInteractive);

/// <summary>
/// A password that checked out, on an account that also has a second factor. The token is deliberately
/// not issued yet — it is issued by /auth/mfa once the code is supplied.
/// </summary>
public sealed record MfaChallengeResponse(bool MfaRequired, string MfaToken);
public sealed record MfaVerifyRequest(string? MfaToken, string? Code);

/// <param name="Code">The one-time handoff code the OIDC callback put in the redirect.</param>
public sealed record OidcExchangeRequest(string? Code);
public sealed record ErrorResponse(string Message);

// ── Account lifecycle ─────────────────────────────────────────────────────────────────────────────

public sealed record InviteRequest(string FullName, string Email, UserRole Role, string? Skills);

/// <param name="Url">
/// The setup link, present only in the response that created it. Only the token's hash is stored, so
/// the product cannot show this again — which is the point.
/// </param>
public sealed record InvitationResponse(
    long UserId, string FullName, string Email, UserRole Role,
    DateTime ExpiresAtUtc, DateTime CreatedAtUtc, string? Url);

public sealed record InvitationPeekResponse(string FullName, string Email, DateTime ExpiresAtUtc);
public sealed record AcceptInviteRequest(string? Password);

// ── Second factor ─────────────────────────────────────────────────────────────────────────────────

public sealed record TotpStatusResponse(bool Available, bool Required, bool Enrolled);
public sealed record TotpSetupResponse(string Secret, string OtpAuthUri);
public sealed record TotpCodeRequest(string? Code);

// ── Passkeys ──────────────────────────────────────────────────────────────────────────────────────

/// <param name="OptionsJson">Passed to navigator.credentials verbatim; the API does not reshape it.</param>
public sealed record PasskeyChallengeResponse(string ChallengeId, string OptionsJson);
public sealed record PasskeyRegisterRequest(string? ChallengeId, string? Label, string? Credential);
public sealed record PasskeyLoginRequest(string? ChallengeId, string? Credential);
public sealed record NotificationPage(IReadOnlyList<Notification> Items, int UnreadCount);

// AOT-safe, source-generated JSON. camelCase + string enums for a clean API surface.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Ticket))]
[JsonSerializable(typeof(Page<Ticket>))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(List<User>))]
[JsonSerializable(typeof(IReadOnlyList<User>))]
[JsonSerializable(typeof(TicketActivity))]
[JsonSerializable(typeof(List<TicketActivity>))]
[JsonSerializable(typeof(IReadOnlyList<TicketActivity>))]
[JsonSerializable(typeof(ResolutionSuggestion))]
[JsonSerializable(typeof(List<ResolutionSuggestion>))]
[JsonSerializable(typeof(IReadOnlyList<ResolutionSuggestion>))]
[JsonSerializable(typeof(TicketCreate))]
[JsonSerializable(typeof(UserCreate))]
[JsonSerializable(typeof(AssignRequest))]
[JsonSerializable(typeof(StatusRequest))]
[JsonSerializable(typeof(CommentRequest))]
[JsonSerializable(typeof(StatsResponse))]
[JsonSerializable(typeof(IdResponse))]
[JsonSerializable(typeof(KnowledgeSearchRequest))]
[JsonSerializable(typeof(SuggestionResult))]
[JsonSerializable(typeof(AiStatus))]
[JsonSerializable(typeof(IndexStats))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(AuthConfigResponse))]
[JsonSerializable(typeof(MfaChallengeResponse))]
[JsonSerializable(typeof(MfaVerifyRequest))]
[JsonSerializable(typeof(OidcExchangeRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(InviteRequest))]
[JsonSerializable(typeof(InvitationResponse))]
[JsonSerializable(typeof(List<InvitationResponse>))]
[JsonSerializable(typeof(InvitationPeekResponse))]
[JsonSerializable(typeof(AcceptInviteRequest))]
[JsonSerializable(typeof(TotpStatusResponse))]
[JsonSerializable(typeof(TotpSetupResponse))]
[JsonSerializable(typeof(TotpCodeRequest))]
[JsonSerializable(typeof(PasskeyChallengeResponse))]
[JsonSerializable(typeof(PasskeyRegisterRequest))]
[JsonSerializable(typeof(PasskeyLoginRequest))]
[JsonSerializable(typeof(IReadOnlyList<PasskeySummary>))]
[JsonSerializable(typeof(TicketAssessment))]
[JsonSerializable(typeof(ResolutionReview))]
[JsonSerializable(typeof(RoutingRecommendation))]
[JsonSerializable(typeof(RoutingCandidate))]
[JsonSerializable(typeof(IReadOnlyList<RoutingCandidate>))]
[JsonSerializable(typeof(Notification))]
[JsonSerializable(typeof(IReadOnlyList<Notification>))]
[JsonSerializable(typeof(NotificationPage))]
[JsonSerializable(typeof(long))]
public partial class AppJsonContext : JsonSerializerContext;
