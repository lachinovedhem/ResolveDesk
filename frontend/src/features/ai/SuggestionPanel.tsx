import { Link } from "react-router-dom";
import { Sparkles, Search, BrainCircuit } from "lucide-react";
import { Badge, Card, CardBody, CardHeader, CardTitle, EmptyState, Skeleton } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import type { ResolutionSuggestion, SuggestionResult } from "@/lib/api";

/**
 * The feature the product exists for: instead of answering from scratch, show what the team already
 * did about tickets like this one, and only then the model's draft built from those.
 *
 * The evidence is deliberately above the draft. An agent should read the real resolutions first and
 * treat the generated text as a summary of them, not as an authority of its own.
 */
export function SuggestionPanel({ result, loading, error }: {
  result: SuggestionResult | null;
  loading: boolean;
  error?: Error | null;
}) {
  const { t } = useI18n();

  return (
    <Card>
      <CardHeader className="flex items-center justify-between gap-3">
        <div>
          <CardTitle className="flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-brand-500" aria-hidden />
            {t("suggestions.title")}
          </CardTitle>
          <p className="mt-1 text-meta text-ink-500">{t("suggestions.subtitle")}</p>
        </div>
        {result && result.matches.length > 0 && (
          <span className="hidden text-micro text-ink-400 sm:block">
            {t("suggestions.meta", {
              strategy: result.strategy,
              count: result.matches.length,
              considered: result.considered,
              ms: result.elapsedMs,
            })}
          </span>
        )}
      </CardHeader>

      <CardBody className="space-y-4">
        {loading && <LoadingRows label={t("suggestions.searching")} />}

        {!loading && error && (
          <p className="text-ui text-danger-ink" role="alert">{error.message}</p>
        )}

        {!loading && !error && (!result || result.matches.length === 0) && (
          <EmptyState
            icon={<Search className="h-8 w-8" aria-hidden />}
            title={t("suggestions.empty")}
          />
        )}

        {!loading && !error && result && result.matches.length > 0 && (
          <>
            <ul className="space-y-3">
              {result.matches.map((match) => <MatchRow key={match.sourceTicketId} match={match} />)}
            </ul>

            {result.answer && (
              <section className="rounded-card border border-brand-200 bg-brand-50 p-4">
                <h3 className="flex items-center gap-2 text-ui font-medium text-ink-900">
                  <BrainCircuit className="h-4 w-4 text-brand-600" aria-hidden />
                  {t("suggestions.answer")}
                  {result.model && <span className="font-mono text-micro text-ink-500">{result.model}</span>}
                </h3>
                <p className="mt-1 text-micro text-ink-500">{t("suggestions.verify")}</p>
                <p className="mt-3 whitespace-pre-wrap text-ui text-ink-800">{result.answer}</p>
              </section>
            )}
          </>
        )}
      </CardBody>
    </Card>
  );
}

function MatchRow({ match }: { match: ResolutionSuggestion }) {
  const { t } = useI18n();
  const tone = match.matchKind === "hybrid" ? "success" : match.matchKind === "semantic" ? "info" : "neutral";

  return (
    <li className="rounded-control border border-border-subtle bg-surface-1 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Link
          to={`/tickets/${match.sourceTicketId}`}
          className="font-mono text-meta text-brand-600 transition-colors hover:text-brand-700"
        >
          {match.sourceReference}
        </Link>
        <div className="flex items-center gap-2">
          <Badge tone={tone}>{t(`suggestions.match.${match.matchKind}`)}</Badge>
          <span className="text-micro text-ink-400">
            {t("suggestions.similarity", { value: Math.round(match.similarity * 100) })}
          </span>
        </div>
      </div>
      <p className="mt-1 text-ui font-medium text-ink-900">{match.title}</p>
      <p className="mt-1 whitespace-pre-wrap text-meta text-ink-600">{match.resolution}</p>
    </li>
  );
}

/** Skeletons rather than a spinner: the panel keeps its shape while the search runs. */
function LoadingRows({ label }: { label: string }) {
  return (
    <div className="space-y-3" aria-busy="true" aria-label={label}>
      {[0, 1, 2].map((row) => (
        <div key={row} className="space-y-2 rounded-control border border-border-subtle p-3">
          <Skeleton className="h-3 w-28" />
          <Skeleton className="h-4 w-3/4" />
          <Skeleton className="h-3 w-full" />
        </div>
      ))}
    </div>
  );
}
