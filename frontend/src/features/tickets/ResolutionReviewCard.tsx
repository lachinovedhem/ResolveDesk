import { useState } from "react";
import { AlertTriangle, Check, Copy, Sparkles, Lightbulb } from "lucide-react";
import { Badge, Card, CardBody, CardHeader, CardTitle, type BadgeTone } from "@/components/ui/primitives";
import { Button } from "@/components/ui/Button";
import { useI18n } from "@/i18n";
import type { ConsistencyVerdict, ResolutionReview } from "@/lib/api";

const VERDICT_TONE: Record<ConsistencyVerdict, BadgeTone> = {
  Consistent: "success",
  Differs: "info",
  Novel: "accent",
  Conflicts: "danger",
};

/**
 * What the model made of a resolution once an agent wrote it: a customer-ready version of their
 * notes, a tidy internal summary, and how it lines up with past practice.
 *
 * The agent's own text is shown separately on the ticket and is never replaced — this is an addition
 * to the record, which is why the polished reply is offered as something to copy rather than
 * something already sent.
 */
export function ResolutionReviewCard({ review }: { review: ResolutionReview }) {
  const { t } = useI18n();
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(review.polishedReply);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard blocked (insecure context or denied) — the text is on screen to select by hand.
    }
  };

  return (
    <Card>
      <CardHeader className="flex flex-wrap items-center justify-between gap-2">
        <CardTitle className="flex items-center gap-2">
          <Sparkles className="h-4 w-4 text-brand-500" aria-hidden />
          {t("review.title")}
        </CardTitle>
        <Badge tone={VERDICT_TONE[review.verdict]}>
          {review.verdict === "Conflicts" && <AlertTriangle className="h-3 w-3" aria-hidden />}
          {t(`review.verdict.${review.verdict}`)}
        </Badge>
      </CardHeader>

      <CardBody className="space-y-4">
        {review.verdictDetail && (
          <p className="rounded-control border border-border-subtle bg-surface-1 p-3 text-meta text-ink-700">
            {review.verdictDetail}
          </p>
        )}

        {review.comparedReferences && (
          <p className="text-micro text-ink-400">
            {t("review.compared", { references: review.comparedReferences })}
          </p>
        )}

        {review.polishedReply && (
          <section>
            <div className="flex items-center justify-between gap-2">
              <h3 className="text-meta font-medium text-ink-600">{t("review.customerReply")}</h3>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => void copy()}
                icon={copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
              >
                {copied ? t("review.copied") : t("review.copy")}
              </Button>
            </div>
            <p className="mt-1 whitespace-pre-wrap rounded-control border border-brand-200 bg-brand-50 p-3
                          text-ui text-ink-800">
              {review.polishedReply}
            </p>
            <p className="mt-1 text-micro text-ink-400">{t("review.verify")}</p>
          </section>
        )}

        {review.internalNote && (
          <section>
            <h3 className="flex items-center gap-1.5 text-meta font-medium text-ink-600">
              <Lightbulb className="h-3.5 w-3.5" aria-hidden />
              {t("review.internalNote")}
            </h3>
            <p className="mt-1 whitespace-pre-wrap text-ui text-ink-800">{review.internalNote}</p>
          </section>
        )}

        <p className="text-micro text-ink-400">
          <span className="font-mono">{review.model}</span>
        </p>
      </CardBody>
    </Card>
  );
}
