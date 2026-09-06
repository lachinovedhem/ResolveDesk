import { useState } from "react";
import { Link } from "react-router-dom";
import { Copy, Gauge, RefreshCw, Timer } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Badge, Card, CardBody, CardHeader, CardTitle, Skeleton } from "@/components/ui/primitives";
import { cn } from "@/lib/cn";
import { useI18n } from "@/i18n";
import { api, type TicketAssessment } from "@/lib/api";
import { PRIORITY_TONE } from "@/lib/display";

/**
 * The model's read on an incoming ticket: how hard, how long, what kind, and whether we are already
 * working on it.
 *
 * Presented as an opinion, not a fact — the confidence is on screen, nothing here has been applied to
 * the ticket, and the model that produced it is named.
 */
export function AssessmentCard({ ticketId, assessment, loading, onUpdated }: {
  ticketId: number;
  assessment: TicketAssessment | null;
  loading: boolean;
  onUpdated: (assessment: TicketAssessment) => void;
}) {
  const { t } = useI18n();
  const [busy, setBusy] = useState(false);

  const rerun = async () => {
    setBusy(true);
    try {
      const result = await api.reassess(ticketId);
      if (result) onUpdated(result);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <CardHeader className="flex items-center justify-between gap-3">
        <CardTitle className="flex items-center gap-2">
          <Gauge className="h-4 w-4 text-brand-500" aria-hidden />
          {t("assessment.title")}
        </CardTitle>
        <Button
          variant="ghost"
          size="sm"
          loading={busy}
          onClick={() => void rerun()}
          icon={<RefreshCw className="h-3.5 w-3.5" />}
          aria-label={t("assessment.rerun")}
        >
          <span className="hidden sm:inline">{t("assessment.rerun")}</span>
        </Button>
      </CardHeader>

      <CardBody className="space-y-4">
        {loading && <Skeleton className="h-28 w-full" />}

        {!loading && !assessment && (
          <p className="text-meta text-ink-500">{t("assessment.pending")}</p>
        )}

        {!loading && assessment && (
          <>
            <p className="text-ui text-ink-800">{assessment.summary}</p>

            <div className="grid grid-cols-2 gap-3">
              <Metric
                icon={<Gauge className="h-4 w-4" aria-hidden />}
                label={t("assessment.difficulty")}
                value={<DifficultyDots value={assessment.difficulty} />}
              />
              <Metric
                icon={<Timer className="h-4 w-4" aria-hidden />}
                label={t("assessment.effort")}
                value={<span className="text-ui-lg font-semibold text-ink-900">
                  {formatMinutes(assessment.estimatedMinutes)}
                </span>}
              />
            </div>

            <div className="flex flex-wrap items-center gap-2">
              {assessment.suggestedCategory && (
                <Badge tone="info">{t("assessment.category")}: {assessment.suggestedCategory}</Badge>
              )}
              {assessment.suggestedPriority && (
                <Badge tone={PRIORITY_TONE[assessment.suggestedPriority]}>
                  {t("assessment.priority")}: {t(`priority.${assessment.suggestedPriority}`)}
                </Badge>
              )}
            </div>

            {assessment.duplicateReference && assessment.duplicateOfTicketId && (
              <Link
                to={`/tickets/${assessment.duplicateOfTicketId}`}
                className="flex items-start gap-2 rounded-control border border-accent-soft bg-accent-soft
                           p-3 transition-colors hover:brightness-95"
              >
                <Copy className="mt-0.5 h-4 w-4 shrink-0 text-accent-ink" aria-hidden />
                <span className="text-meta text-accent-ink">
                  {t("assessment.duplicate", { reference: assessment.duplicateReference })}
                </span>
              </Link>
            )}

            <ConfidenceBar value={assessment.confidence} model={assessment.model} />
          </>
        )}
      </CardBody>
    </Card>
  );
}

function Metric({ icon, label, value }: { icon: React.ReactNode; label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-control border border-border-subtle bg-surface-1 p-3">
      <p className="flex items-center gap-1.5 text-micro text-ink-500">{icon}{label}</p>
      <div className="mt-1">{value}</div>
    </div>
  );
}

/** Five dots rather than "3/5": readable at a glance, and it still works in greyscale. */
function DifficultyDots({ value }: { value: number }) {
  const { t } = useI18n();
  return (
    <span className="flex items-center gap-1" aria-label={t("assessment.difficultyValue", { value })}>
      {[1, 2, 3, 4, 5].map((step) => (
        <span
          key={step}
          className={cn(
            "h-2.5 w-2.5 rounded-full",
            step <= value
              ? value >= 4 ? "bg-danger" : value === 3 ? "bg-warning" : "bg-success"
              : "bg-ink-200",
          )}
        />
      ))}
    </span>
  );
}

function ConfidenceBar({ value, model }: { value: number; model: string }) {
  const { t } = useI18n();
  const percent = Math.round(value * 100);
  return (
    <div>
      <div className="flex items-center justify-between text-micro text-ink-400">
        <span>{t("assessment.confidence", { value: percent })}</span>
        <span className="font-mono">{model}</span>
      </div>
      <div className="mt-1 h-1 w-full overflow-hidden rounded-full bg-ink-200">
        <div
          className={cn("h-full rounded-full", percent >= 60 ? "bg-brand-500" : "bg-warning")}
          style={{ width: `${percent}%` }}
        />
      </div>
      {percent < 40 && <p className="mt-1 text-micro text-ink-400">{t("assessment.lowConfidence")}</p>}
    </div>
  );
}

function formatMinutes(minutes: number) {
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}
