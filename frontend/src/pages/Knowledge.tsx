import { useState, type FormEvent } from "react";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, Field, PageHeader, Textarea } from "@/components/ui/primitives";
import { SuggestionPanel } from "@/features/ai/SuggestionPanel";
import { useI18n } from "@/i18n";
import { api, type SuggestionResult } from "@/lib/api";

/** Look something up without creating a ticket — the same hybrid retrieval the MCP server exposes. */
export function KnowledgePage() {
  const { t } = useI18n();
  const [problem, setProblem] = useState("");
  const [result, setResult] = useState<SuggestionResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (problem.trim().length < 4) return;
    setLoading(true);
    setError(null);
    try {
      setResult(await api.searchKnowledge(problem.trim(), undefined, 8));
    } catch (caught) {
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    } finally {
      setLoading(false);
    }
  };

  return (
    <>
      <PageHeader title={t("knowledge.title")} subtitle={t("knowledge.subtitle")} />

      <div className="space-y-6">
        <Card>
          <CardBody>
            <form onSubmit={onSubmit} className="space-y-4">
              <Field label={t("knowledge.title")} htmlFor="problem">
                <Textarea
                  id="problem"
                  autoFocus
                  value={problem}
                  onChange={(event) => setProblem(event.target.value)}
                  placeholder={t("knowledge.placeholder")}
                />
              </Field>
              <Button
                type="submit"
                variant="primary"
                loading={loading}
                icon={<Search className="h-4 w-4" />}
                disabled={problem.trim().length < 4}
              >
                {t("action.search")}
              </Button>
            </form>
          </CardBody>
        </Card>

        {(result || loading || error) && (
          <SuggestionPanel result={result} loading={loading} error={error} />
        )}
      </div>
    </>
  );
}
