/** Wire types mirroring the API's source-generated JSON: camelCase properties, string enums. */

export type TicketStatus = "Open" | "Assigned" | "InProgress" | "WaitingCustomer" | "Resolved" | "Closed";
export type TicketPriority = "Low" | "Normal" | "High" | "Urgent";
export type TicketSource = "Phone" | "Email" | "Chat" | "Portal" | "Internal";
export type UserRole = "Agent" | "Coordinator" | "Admin";
export type ActivityKind = "Comment" | "StatusChange" | "Assignment" | "Resolution" | "AiSuggestion";

export const TICKET_STATUSES: TicketStatus[] =
  ["Open", "Assigned", "InProgress", "WaitingCustomer", "Resolved", "Closed"];
export const TICKET_PRIORITIES: TicketPriority[] = ["Low", "Normal", "High", "Urgent"];
export const TICKET_SOURCES: TicketSource[] = ["Phone", "Email", "Chat", "Portal", "Internal"];

export interface Ticket {
  id: number;
  reference: string;
  title: string;
  description: string;
  status: TicketStatus;
  priority: TicketPriority;
  source: TicketSource;
  category?: string | null;
  customerName: string;
  customerContact?: string | null;
  assigneeId?: number | null;
  coordinatorId?: number | null;
  resolution?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  slaDueAtUtc?: string | null;
  resolvedAtUtc?: string | null;
}

export interface TicketActivity {
  id: number;
  ticketId: number;
  authorId?: number | null;
  kind: ActivityKind;
  body: string;
  createdAtUtc: string;
}

export interface User {
  id: number;
  fullName: string;
  email: string;
  role: UserRole;
  skills?: string | null;
  isActive: boolean;
  createdAtUtc: string;
}

export interface Page<T> { items: T[]; nextCursor: number | null; hasMore: boolean }

export interface ResolutionSuggestion {
  sourceTicketId: number;
  sourceReference: string;
  title: string;
  resolution: string;
  similarity: number;
  matchKind: "semantic" | "keyword" | "hybrid";
}

export interface SuggestionResult {
  matches: ResolutionSuggestion[];
  answer?: string | null;
  strategy: "hybrid" | "semantic" | "keyword" | "none";
  model?: string | null;
  considered: number;
  elapsedMs: number;
}

export interface AiStatus {
  enabled: boolean;
  chatProvider: string;
  chatModel: string;
  chatReachable: boolean;
  embeddingProvider: string;
  embeddingModel: string;
  embeddingDimensions: number;
  embeddingReachable: boolean;
  vectorSearchAvailable: boolean;
  detail?: string | null;
}

/** What the model concluded about a ticket. Advisory — never applied automatically. */
export interface TicketAssessment {
  ticketId: number;
  difficulty: number;            // 1..5
  estimatedMinutes: number;
  suggestedCategory?: string | null;
  suggestedPriority?: TicketPriority | null;
  duplicateOfTicketId?: number | null;
  duplicateReference?: string | null;
  summary: string;
  confidence: number;            // 0..1
  model: string;
  createdAtUtc: string;
}

export type ConsistencyVerdict = "Consistent" | "Differs" | "Novel" | "Conflicts";

export interface ResolutionReview {
  ticketId: number;
  polishedReply: string;
  internalNote: string;
  verdict: ConsistencyVerdict;
  verdictDetail: string;
  comparedReferences: string;
  model: string;
  createdAtUtc: string;
}

export interface RoutingCandidate {
  userId: number;
  fullName: string;
  skills?: string | null;
  openTickets: number;
  solvedSimilar: number;
  skillMatches: number;
  score: number;
  reason: string;
}

export interface RoutingRecommendation {
  assigneeId?: number | null;
  assigneeName?: string | null;
  reason: string;
  candidates: RoutingCandidate[];
  /** When the analysis ran. The result is stored, so this can be old. */
  generatedAtUtc: string;
}

export type NotificationKind =
  | "Assigned" | "StatusChanged" | "Comment" | "SlaRisk" | "ResolutionReviewed" | "DuplicateDetected";

export interface AppNotification {
  id: number;
  userId: number;
  kind: NotificationKind;
  ticketId?: number | null;
  title: string;
  body: string;
  createdAtUtc: string;
  readAtUtc?: string | null;
}

export interface NotificationPage { items: AppNotification[]; unreadCount: number }

export interface Stats {
  open: number; assigned: number; inProgress: number;
  waitingCustomer: number; resolved: number; closed: number;
}

export type AuthMethodName = "Password" | "Oidc" | "Passkey";

export interface AuthConfig {
  enabled: boolean;
  /** Which backend checks a password. Only meaningful when "Password" is among the methods. */
  provider: "Local" | "Ldap" | "Oidc";
  authority?: string | null;
  audience?: string | null;
  acceptsPassword: boolean;
  /** Every method this deployment offers. The sign-in screen renders all of them, not one. */
  methods: AuthMethodName[];
  passkeysEnabled: boolean;
  totpAvailable: boolean;
  totpRequired: boolean;
  /** True when this API runs the SSO flow itself, so /auth/oidc/start is a real route. */
  oidcInteractive: boolean;
}

/** A correct password on an account with a second factor: not a session yet. */
export interface MfaChallenge {
  mfaRequired: true;
  mfaToken: string;
}

export interface Invitation {
  userId: number;
  fullName: string;
  email: string;
  role: UserRole;
  expiresAtUtc: string;
  createdAtUtc: string;
  /** Present only in the response that created it — never in a listing. */
  url?: string | null;
}

export interface InvitationPeek { fullName: string; email: string; expiresAtUtc: string }
export interface TotpStatus { available: boolean; required: boolean; enrolled: boolean }
export interface TotpSetup { secret: string; otpAuthUri: string }
export interface Passkey { id: number; label: string; createdAtUtc: string; lastUsedAtUtc?: string | null }
export interface PasskeyChallenge { challengeId: string; optionsJson: string }

export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  userId: number;
  fullName: string;
  email: string;
  role: UserRole;
}

export interface TicketFilter {
  status?: TicketStatus;
  priority?: TicketPriority;
  assigneeId?: number;
  search?: string;
  cursor?: number;
  limit?: number;
}

/** Carries the HTTP status so callers can tell "signed out" apart from "server is unhappy". */
export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
    this.name = "ApiError";
  }
}

const TOKEN_KEY = "resolvedesk.token";

export const tokenStore = {
  get: () => { try { return localStorage.getItem(TOKEN_KEY); } catch { return null; } },
  set: (token: string) => { try { localStorage.setItem(TOKEN_KEY, token); } catch { /* private mode */ } },
  clear: () => { try { localStorage.removeItem(TOKEN_KEY); } catch { /* private mode */ } },
};

/** Notified on 401 so the shell can drop to the login screen from anywhere. */
let onUnauthorized: (() => void) | null = null;
export function setUnauthorizedHandler(handler: () => void) { onUnauthorized = handler; }

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = tokenStore.get();
  const response = await fetch(`/api/v1${path}`, {
    ...init,
    headers: {
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  });

  if (response.status === 401) {
    tokenStore.clear();
    onUnauthorized?.();
  }

  if (!response.ok) {
    // The API answers with either ProblemDetails or our own { message } shape.
    const text = await response.text();
    let message = text || response.statusText;
    try {
      const parsed = JSON.parse(text);
      message = parsed.message ?? parsed.detail ?? parsed.title ?? message;
    } catch { /* not JSON — use the raw text */ }
    throw new ApiError(response.status, message);
  }

  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>);
}

const query = (params: Record<string, string | number | undefined>) => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : "";
};

export const api = {
  authConfig: () => request<AuthConfig>("/auth/config"),
  // Either a session or a second-factor challenge; the caller has to look at which.
  login: (username: string, password: string) =>
    request<LoginResponse | MfaChallenge>("/auth/login", {
      method: "POST", body: JSON.stringify({ username, password }) }),
  verifyMfa: (mfaToken: string, code: string) =>
    request<LoginResponse>("/auth/mfa", { method: "POST", body: JSON.stringify({ mfaToken, code }) }),
  // The SSO callback hands the browser a one-time code, never the session itself.
  exchangeOidcCode: (code: string) =>
    request<LoginResponse>("/auth/oidc/exchange", { method: "POST", body: JSON.stringify({ code }) }),

  // Invitations: an account is created by someone who has one, and its first credential arrives
  // over a link that stops working the moment it is used.
  invite: (body: { fullName: string; email: string; role: UserRole; skills?: string | null }) =>
    request<Invitation>("/invitations", { method: "POST", body: JSON.stringify(body) }),
  invitations: () => request<Invitation[]>("/invitations"),
  resendInvite: (userId: number) =>
    request<Invitation>(`/invitations/${userId}/resend`, { method: "POST" }),
  revokeInvite: (userId: number) =>
    request<void>(`/invitations/${userId}`, { method: "DELETE" }),
  peekInvite: (token: string) => request<InvitationPeek>(`/invite/${encodeURIComponent(token)}`),
  acceptInvite: (token: string, password: string) =>
    request<LoginResponse>(`/invite/${encodeURIComponent(token)}`, {
      method: "POST", body: JSON.stringify({ password }) }),

  totpStatus: () => request<TotpStatus>("/account/totp"),
  totpSetup: () => request<TotpSetup>("/account/totp/setup", { method: "POST" }),
  totpEnable: (code: string) =>
    request<void>("/account/totp/enable", { method: "POST", body: JSON.stringify({ code }) }),
  totpDisable: (code: string) =>
    request<void>("/account/totp/disable", { method: "POST", body: JSON.stringify({ code }) }),

  passkeys: () => request<Passkey[]>("/account/passkeys"),
  deletePasskey: (id: number) => request<void>(`/account/passkeys/${id}`, { method: "DELETE" }),
  passkeyRegisterBegin: () =>
    request<PasskeyChallenge>("/account/passkeys/register/begin", { method: "POST" }),
  passkeyRegisterFinish: (challengeId: string, label: string, credential: string) =>
    request<void>("/account/passkeys/register/finish", {
      method: "POST", body: JSON.stringify({ challengeId, label, credential }) }),
  passkeyLoginBegin: () => request<PasskeyChallenge>("/auth/passkey/begin", { method: "POST" }),
  passkeyLoginFinish: (challengeId: string, credential: string) =>
    request<LoginResponse>("/auth/passkey/finish", {
      method: "POST", body: JSON.stringify({ challengeId, credential }) }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>("/auth/password", { method: "POST", body: JSON.stringify({ currentPassword, newPassword }) }),

  listTickets: (filter: TicketFilter = {}) =>
    request<Page<Ticket>>(`/tickets${query({ ...filter })}`),
  getTicket: (id: number) => request<Ticket>(`/tickets/${id}`),
  createTicket: (body: {
    title: string; description: string; priority: TicketPriority; source: TicketSource;
    category?: string; customerName: string; customerContact?: string;
  }) => request<Ticket>("/tickets", { method: "POST", body: JSON.stringify(body) }),
  assign: (id: number, assigneeId: number, coordinatorId: number) =>
    request<void>(`/tickets/${id}/assign`, { method: "POST", body: JSON.stringify({ assigneeId, coordinatorId }) }),
  setStatus: (id: number, status: TicketStatus, resolution?: string) =>
    request<void>(`/tickets/${id}/status`, { method: "POST", body: JSON.stringify({ status, resolution }) }),
  activities: (id: number) => request<TicketActivity[]>(`/tickets/${id}/activities`),
  addComment: (id: number, body: string, authorId?: number) =>
    request<{ id: number }>(`/tickets/${id}/comments`, { method: "POST", body: JSON.stringify({ authorId, body }) }),
  suggestions: (id: number, limit = 5) =>
    request<SuggestionResult>(`/tickets/${id}/suggestions${query({ limit })}`),

  searchKnowledge: (title: string, description?: string, limit = 5) =>
    request<SuggestionResult>("/knowledge/search", {
      method: "POST", body: JSON.stringify({ title, description, limit }),
    }),

  // 204 (no assessment yet, or no chat model configured) surfaces as null, not an error.
  assessment: (id: number) => request<TicketAssessment | undefined>(`/tickets/${id}/assessment`),
  reassess: (id: number) => request<TicketAssessment | undefined>(`/tickets/${id}/assessment`, { method: "POST" }),
  // GET serves the stored analysis; POST re-runs it. Recomputing on every view cost several
  // seconds and a model call, for an answer that rarely changes between page loads.
  routing: (id: number) => request<RoutingRecommendation>(`/tickets/${id}/routing`),
  rerouting: (id: number) => request<RoutingRecommendation>(`/tickets/${id}/routing`, { method: "POST" }),
  resolutionReview: (id: number) => request<ResolutionReview | undefined>(`/tickets/${id}/resolution-review`),

  notifications: (userId?: number, unreadOnly = false, limit = 50) =>
    request<NotificationPage>(`/notifications${query({ userId, unreadOnly: String(unreadOnly), limit })}`),
  markNotificationRead: (id: number, userId?: number) =>
    request<void>(`/notifications/${id}/read${query({ userId })}`, { method: "POST" }),
  markAllNotificationsRead: (userId?: number) =>
    request<void>(`/notifications/read-all${query({ userId })}`, { method: "POST" }),

  users: (role?: UserRole) => request<User[]>(`/users${query({ role })}`),
  stats: () => request<Stats>("/stats"),
  aiStatus: () => request<AiStatus>("/ai/status"),
};
