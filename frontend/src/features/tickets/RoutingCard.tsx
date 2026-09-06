import { useState } from "react";
import { ChevronDown, ChevronUp, RefreshCw, Users } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, CardHeader, CardTitle, Skeleton } from "@/components/ui/primitives";
import { cn } from "@/lib/cn";
import { useI18n } from "@/i18n";
import { formatDuration } from "@/lib/display";
import type { RoutingRecommendation } from "@/lib/api";

/**
 * Who should take this ticket, and why.
 *
 * The three numbers behind every candidate are on screen because the recommendation is arithmetic,
 * not a model verdict — a coordinator who can see that someone came top on an empty queue rather
 * than on experience will overrule it, which is exactly the point.
 */
export function RoutingCard({ recommendation, loading, onAssign, assigning, onRerun, rerunning }: {
  recommendation: RoutingRecommendation | null;
  loading: boolean;
  onAssign: (assigneeId: number) => void;
  assigning: boolean;
  onRerun: () => void;
  rerunning: boolean;
}) {
  const { t, language } = useI18n();
  const [expanded, setExpanded] = useState(false);

  const others = recommendation?.candidates.slice(1) ?? [];

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-2">
          <CardTitle className="flex items-center gap-2">
            <Users className="h-4 w-4 text-brand-500" aria-hidden />
            {t("routing.title")}
          </CardTitle>
          <Button variant="ghost" size="sm" onClick={onRerun} loading={rerunning} disabled={loading}>
            <RefreshCw className="h-3.5 w-3.5" aria-hidden />
            {t("routing.rerun")}
          </Button>
        </div>
        <p className="mt-1 text-meta text-ink-500">{t("routing.subtitle")}</p>
        {/* The analysis is stored, so it can be old. Saying when it ran is what makes the
            re-run button meaningful rather than decorative. */}
        {recommendation && !loading && (
          <p className="mt-1 text-micro text-ink-400">
            {t("routing.analysedAt", { time: formatDuration(recommendation.generatedAtUtc, language) ?? "" })}
          </p>
        )}
      </CardHeader>

      <CardBody className="space-y-3">
        {loading && <Skeleton className="h-24 w-full" />}

        {!loading && !recommendation?.assigneeId && (
          <p className="text-meta text-ink-500">{recommendation?.reason ?? t("routing.none")}</p>
        )}

        {!loading && recommendation?.assigneeId && (
          <>
            <div className="rounded-control border border-brand-200 bg-brand-50 p-3">
              <p className="text-ui font-medium text-ink-900">{recommendation.assigneeName}</p>
              <p className="mt-1 text-meta text-ink-600">{recommendation.reason}</p>
              <Button
                variant="primary"
                size="sm"
                className="mt-3 w-full"
                loading={assigning}
                onClick={() => onAssign(recommendation.assigneeId!)}
              >
                {t("routing.assignRecommended")}
              </Button>
            </div>

            {others.length > 0 && (
              <>
                <button
                  onClick={() => setExpanded((previous) => !previous)}
                  className="flex min-h-[44px] w-full items-center justify-between rounded-control px-2
                             text-meta text-ink-600 transition-colors hover:bg-brand-50 dark:hover:bg-brand-100"
                  aria-expanded={expanded}
                >
                  {t("routing.others", { count: others.length })}
                  {expanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                </button>

                {expanded && (
                  <ul className="space-y-2">
                    {others.map((candidate) => (
                      <li
                        key={candidate.userId}
                        className="rounded-control border border-border-subtle bg-surface-1 p-3"
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-ui text-ink-900">{candidate.fullName}</span>
                          <ScoreBar value={candidate.score} />
                        </div>
                        <p className="mt-1 text-micro text-ink-500">{candidate.reason}</p>
                        <Button
                          variant="ghost"
                          size="sm"
                          className="mt-2"
                          disabled={assigning}
                          onClick={() => onAssign(candidate.userId)}
                        >
                          {t("action.assign")}
                        </Button>
                      </li>
                    ))}
                  </ul>
                )}
              </>
            )}
          </>
        )}
      </CardBody>
    </Card>
  );
}

function ScoreBar({ value }: { value: number }) {
  const percent = Math.round(value * 100);
  return (
    <span className="flex items-center gap-2">
      <span className="h-1.5 w-16 overflow-hidden rounded-full bg-ink-200">
        <span
          className={cn("block h-full rounded-full", percent >= 50 ? "bg-brand-500" : "bg-ink-400")}
          style={{ width: `${Math.max(percent, 4)}%` }}
        />
      </span>
      <span className="w-8 text-right font-mono text-micro text-ink-400">{percent}</span>
    </span>
  );
}
