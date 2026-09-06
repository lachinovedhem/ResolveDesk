import { useCallback, useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { AlertTriangle, MessageSquare } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Modal } from "@/components/ui/Modal";
import {
  Badge, Card, CardBody, CardHeader, CardTitle, EmptyState,
  Field, PageHeader, Select, Skeleton, Textarea,
} from "@/components/ui/primitives";
import { SuggestionPanel } from "@/features/ai/SuggestionPanel";
import { AssessmentCard } from "@/features/tickets/AssessmentCard";
import { ResolutionReviewCard } from "@/features/tickets/ResolutionReviewCard";
import { RoutingCard } from "@/features/tickets/RoutingCard";
import { useI18n } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { useAsync } from "@/lib/hooks";
import {
  api, TICKET_STATUSES,
  type SuggestionResult, type Ticket, type TicketActivity, type TicketAssessment,
  type RoutingRecommendation, type TicketStatus, type User,
} from "@/lib/api";
import { PRIORITY_TONE, STATUS_TONE, formatDateTime, isOverdue } from "@/lib/display";

export function TicketDetailPage() {
  const { id } = useParams<{ id: string }>();
  const ticketId = Number(id);
  const { t, language } = useI18n();
  const { session } = useAuth();

  const ticket = useAsync(() => api.getTicket(ticketId), [ticketId]);
  const activities = useAsync(() => api.activities(ticketId), [ticketId]);
  const agents = useAsync(() => api.users("Agent"), []);
  const assessment = useAsync(() => api.assessment(ticketId), [ticketId]);
  const routing = useAsync(() => api.routing(ticketId), [ticketId]);
  const review = useAsync(() => api.resolutionReview(ticketId), [ticketId]);

  // The re-run button returns a fresh assessment; hold it locally so the card updates without a refetch.
  const [assessmentOverride, setAssessmentOverride] = useState<TicketAssessment | null>(null);
  const [routingOverride, setRoutingOverride] = useState<RoutingRecommendation | null>(null);
  const [rerunning, setRerunning] = useState(false);

  // Suggestions are fetched separately: a slow local model must not hold up the ticket itself.
  const [suggestions, setSuggestions] = useState<SuggestionResult | null>(null);
  const [suggestionsLoading, setSuggestionsLoading] = useState(true);
  const [suggestionsError, setSuggestionsError] = useState<Error | null>(null);

  useEffect(() => {
    let current = true;
    setSuggestionsLoading(true);
    setSuggestionsError(null);
    api.suggestions(ticketId, 5)
      .then((result) => { if (current) setSuggestions(result); })
      .catch((error: Error) => { if (current) setSuggestionsError(error); })
      .finally(() => { if (current) setSuggestionsLoading(false); });
    return () => { current = false; };
  }, [ticketId]);

  const refresh = useCallback(() => {
    ticket.reload();
    activities.reload();
  }, [ticket, activities]);

  if (ticket.loading) return <DetailSkeleton />;
  if (ticket.error || !ticket.data) {
    return (
      <Card>
        <EmptyState
          icon={<AlertTriangle className="h-10 w-10" aria-hidden />}
          title={t("error.notFound")}
          description={ticket.error?.message}
        />
      </Card>
    );
  }

  const data = ticket.data;
  const overdue = isOverdue(data.slaDueAtUtc, data.status);

  return (
    <>
      <PageHeader
        title={data.title}
        subtitle={data.reference}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={STATUS_TONE[data.status]}>{t(`status.${data.status}`)}</Badge>
            <Badge tone={PRIORITY_TONE[data.priority]}>{t(`priority.${data.priority}`)}</Badge>
            {overdue && (
              <Badge tone="danger">
                <AlertTriangle className="h-3 w-3" aria-hidden />{t("ticket.overdue")}
              </Badge>
            )}
          </div>
        }
      />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <div className="space-y-6">
          <Card>
            <CardHeader><CardTitle>{t("ticket.description")}</CardTitle></CardHeader>
            <CardBody>
              <p className="whitespace-pre-wrap text-ui text-ink-800">{data.description}</p>
            </CardBody>
          </Card>

          <SuggestionPanel result={suggestions} loading={suggestionsLoading} error={suggestionsError} />

          {review.data && <ResolutionReviewCard review={review.data} />}

          <TimelineCard
            activities={activities.data}
            loading={activities.loading}
            ticketId={ticketId}
            authorId={session?.userId}
            onPosted={refresh}
          />
        </div>

        <div className="space-y-6">
          <AssessmentCard
            ticketId={ticketId}
            // A 204 (not assessed yet) arrives as undefined; the card only distinguishes "have it" from "don't".
            assessment={assessmentOverride ?? assessment.data ?? null}
            loading={assessment.loading}
            onUpdated={setAssessmentOverride}
          />

          {/* Routing only matters while the ticket still needs someone. */}
          {data.status !== "Resolved" && data.status !== "Closed" && (
            <RoutingCard
              recommendation={routingOverride ?? routing.data}
              loading={routing.loading}
              assigning={false}
              rerunning={rerunning}
              onRerun={() => {
                setRerunning(true);
                void api.rerouting(ticketId)
                  .then(setRoutingOverride)
                  .finally(() => setRerunning(false));
              }}
              onAssign={(assigneeId) => {
                if (!session?.userId) return;
                void api.assign(ticketId, assigneeId, session.userId).then(refresh);
              }}
            />
          )}

          <Card>
            <CardHeader><CardTitle>{t("ticket.customer")}</CardTitle></CardHeader>
            <CardBody className="space-y-3 text-ui">
              <Detail label={t("ticket.customer")} value={data.customerName} />
              <Detail label={t("ticket.contact")} value={data.customerContact ?? "—"} />
              <Detail label={t("ticket.source")} value={t(`source.${data.source}`)} />
              <Detail label={t("ticket.category")} value={data.category ?? "—"} />
              <Detail label={t("ticket.created")} value={formatDateTime(data.createdAtUtc, language)} />
              <Detail label={t("ticket.slaDue")} value={formatDateTime(data.slaDueAtUtc, language)} />
              <Detail label={t("ticket.resolvedAt")} value={formatDateTime(data.resolvedAtUtc, language)} />
            </CardBody>
          </Card>

          <AssignmentCard ticket={data} agents={agents.data ?? []} coordinatorId={session?.userId} onChanged={refresh} />
        </div>
      </div>
    </>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <span className="shrink-0 text-meta text-ink-500">{label}</span>
      <span className="text-right text-ui text-ink-800">{value}</span>
    </div>
  );
}

/** Manual assignment and status changes — the coordinator's controls, beside the recommendation. */
function AssignmentCard({ ticket, agents, coordinatorId, onChanged }: {
  ticket: Ticket; agents: User[]; coordinatorId?: number; onChanged: () => void;
}) {
  const { t } = useI18n();
  const [assignee, setAssignee] = useState(String(ticket.assigneeId ?? ""));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resolveOpen, setResolveOpen] = useState(false);
  const [resolution, setResolution] = useState("");

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      onChanged();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t("error.generic"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <CardHeader><CardTitle>{t("ticket.assignee")}</CardTitle></CardHeader>
      <CardBody className="space-y-4">
        <Field label={t("ticket.assignee")} htmlFor="assignee" error={error}>
          <Select id="assignee" value={assignee} onChange={(event) => setAssignee(event.target.value)}>
            <option value="">{t("ticket.unassigned")}</option>
            {agents.map((agent) => (
              <option key={agent.id} value={agent.id}>{agent.fullName}</option>
            ))}
          </Select>
        </Field>

        <Button
          variant="primary"
          className="w-full"
          loading={busy}
          disabled={!assignee || !coordinatorId}
          onClick={() => void run(() => api.assign(ticket.id, Number(assignee), coordinatorId!))}
        >
          {t("action.assign")}
        </Button>

        <Field label={t("ticket.status")} htmlFor="status">
          <Select
            id="status"
            value={ticket.status}
            onChange={(event) => {
              const next = event.target.value as TicketStatus;
              // Resolving without writing down how is what breaks the knowledge base, so it is a
              // separate, deliberate step rather than a dropdown change.
              if (next === "Resolved" || next === "Closed") setResolveOpen(true);
              else void run(() => api.setStatus(ticket.id, next));
            }}
          >
            {TICKET_STATUSES.map((value) => (
              <option key={value} value={value}>{t(`status.${value}`)}</option>
            ))}
          </Select>
        </Field>

        {ticket.resolution && (
          <div>
            <p className="text-meta font-medium text-ink-600">{t("ticket.resolution")}</p>
            <p className="mt-1 whitespace-pre-wrap text-ui text-ink-800">{ticket.resolution}</p>
          </div>
        )}
      </CardBody>

      <Modal
        open={resolveOpen}
        onClose={() => setResolveOpen(false)}
        title={t("action.resolve")}
        footer={
          <div className="flex justify-end gap-2">
            <Button onClick={() => setResolveOpen(false)}>{t("action.cancel")}</Button>
            <Button
              variant="primary"
              loading={busy}
              disabled={resolution.trim().length < 10}
              onClick={() => void run(async () => {
                await api.setStatus(ticket.id, "Resolved", resolution.trim());
                setResolveOpen(false);
                setResolution("");
              })}
            >
              {t("action.resolve")}
            </Button>
          </div>
        }
      >
        <Field
          label={t("ticket.resolution")}
          htmlFor="resolution"
          hint={t("suggestions.subtitle")}
        >
          <Textarea
            id="resolution"
            value={resolution}
            onChange={(event) => setResolution(event.target.value)}
            autoFocus
          />
        </Field>
      </Modal>
    </Card>
  );
}

function TimelineCard({ activities, loading, ticketId, authorId, onPosted }: {
  activities: TicketActivity[] | null; loading: boolean;
  ticketId: number; authorId?: number; onPosted: () => void;
}) {
  const { t, language } = useI18n();
  const [body, setBody] = useState("");
  const [busy, setBusy] = useState(false);

  const post = async () => {
    if (!body.trim()) return;
    setBusy(true);
    try {
      await api.addComment(ticketId, body.trim(), authorId);
      setBody("");
      onPosted();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <CardHeader><CardTitle>{t("ticket.timeline")}</CardTitle></CardHeader>
      <CardBody className="space-y-4">
        {loading && <Skeleton className="h-24 w-full" />}

        {!loading && (!activities || activities.length === 0) && (
          <EmptyState icon={<MessageSquare className="h-8 w-8" aria-hidden />} title={t("empty.timeline")} />
        )}

        {!loading && activities && activities.length > 0 && (
          <ol className="space-y-3">
            {activities.map((activity) => (
              <li key={activity.id} className="rounded-control border border-border-subtle bg-surface-1 p-3">
                <div className="flex items-center justify-between gap-2 text-micro text-ink-400">
                  <span>{activity.kind}</span>
                  <span>{formatDateTime(activity.createdAtUtc, language)}</span>
                </div>
                <p className="mt-1 whitespace-pre-wrap text-ui text-ink-800">{activity.body}</p>
              </li>
            ))}
          </ol>
        )}

        <div className="space-y-2">
          <Textarea
            value={body}
            onChange={(event) => setBody(event.target.value)}
            placeholder={t("action.comment")}
            aria-label={t("action.comment")}
          />
          <Button variant="primary" loading={busy} disabled={!body.trim()} onClick={() => void post()}>
            {t("action.comment")}
          </Button>
        </div>
      </CardBody>
    </Card>
  );
}

function DetailSkeleton() {
  return (
    <div className="space-y-6" aria-busy="true">
      <Skeleton className="h-10 w-2/3" />
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <div className="space-y-6">
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
        <Skeleton className="h-72 w-full" />
      </div>
    </div>
  );
}
