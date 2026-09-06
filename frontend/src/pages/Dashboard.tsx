import { Link } from "react-router-dom";
import { Card, CardBody, PageHeader, Skeleton } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import { useAsync } from "@/lib/hooks";
import { api, type Stats, type TicketStatus } from "@/lib/api";
import { STATUS_TONE } from "@/lib/display";
import { cn } from "@/lib/cn";

const CARDS: { key: keyof Stats; status: TicketStatus }[] = [
  { key: "open", status: "Open" },
  { key: "assigned", status: "Assigned" },
  { key: "inProgress", status: "InProgress" },
  { key: "waitingCustomer", status: "WaitingCustomer" },
  { key: "resolved", status: "Resolved" },
  { key: "closed", status: "Closed" },
];

// Same cool-hue scale as the status badges, so a count and a badge for the same stage read as one thing.
const ACCENT: Record<ReturnType<typeof toneOf>, string> = {
  open: "border-l-status-open",
  progress: "border-l-status-progress",
  done: "border-l-status-done",
};

function toneOf(status: TicketStatus) {
  return STATUS_TONE[status] as "open" | "progress" | "done";
}

export function DashboardPage() {
  const { t } = useI18n();
  const stats = useAsync(() => api.stats(), []);
  const knowledge = useAsync(() => api.aiStatus(), []);

  return (
    <>
      <PageHeader title={t("dashboard.title")} subtitle={t("dashboard.subtitle")} />

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-3 xl:grid-cols-6">
        {CARDS.map(({ key, status }) => (
          <Link key={key} to={`/tickets?status=${status}`} className="focus-visible:outline-none">
            <Card className={cn("border-l-4 transition-colors hover:bg-brand-50 dark:hover:bg-brand-100", ACCENT[toneOf(status)])}>
              <CardBody className="px-4 py-4">
                <p className="text-meta text-ink-500">{t(`status.${status}`)}</p>
                {stats.loading
                  ? <Skeleton className="mt-2 h-8 w-12" />
                  : <p className="mt-1 text-title font-semibold text-ink-900">{stats.data?.[key] ?? 0}</p>}
              </CardBody>
            </Card>
          </Link>
        ))}
      </div>

      {stats.error && (
        <p className="mt-4 text-ui text-danger-ink" role="alert">{stats.error.message}</p>
      )}

      <Card className="mt-6">
        <CardBody className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-ui font-medium text-ink-900">{t("settings.ai")}</p>
            <p className="mt-1 text-meta text-ink-500">
              {knowledge.loading ? t("common.loading") : knowledge.data?.detail ?? t("settings.disabled")}
            </p>
          </div>
          <Link to="/settings" className="text-meta text-brand-600 transition-colors hover:text-brand-700">
            {t("nav.settings")}
          </Link>
        </CardBody>
      </Card>
    </>
  );
}
