import { useState, type FormEvent } from "react";
import { CheckCircle2, XCircle } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Badge, Card, CardBody, CardHeader, CardTitle, Field, Input, PageHeader, Skeleton } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { PasskeyCard, TwoFactorCard } from "@/features/account/SecurityCards";
import { TeamCard } from "@/features/account/TeamCard";
import { useAsync } from "@/lib/hooks";
import { api } from "@/lib/api";

export function SettingsPage() {
  const { t } = useI18n();
  const { config, session } = useAuth();
  const status = useAsync(() => api.aiStatus(), []);

  return (
    <>
      <PageHeader title={t("settings.title")} />

      <div className="grid gap-6 lg:grid-cols-2">
        {/* Credentials the signed-in person manages for themselves. */}
        <PasskeyCard />
        <TwoFactorCard />
        {/* Only coordinators and admins can reach the endpoints behind this; the card shows an
            empty list to anyone else rather than pretending the feature does not exist. */}
        {(session?.role === "Coordinator" || session?.role === "Admin") && <TeamCard />}

        <Card>
          <CardHeader><CardTitle>{t("settings.ai")}</CardTitle></CardHeader>
          <CardBody className="space-y-4">
            {status.loading && <Skeleton className="h-32 w-full" />}

            {status.error && <p className="text-ui text-danger-ink" role="alert">{status.error.message}</p>}

            {status.data && (
              <>
                <Row
                  label={t("settings.chat")}
                  value={`${status.data.chatProvider} / ${status.data.chatModel || "—"}`}
                  ok={status.data.chatReachable}
                />
                <Row
                  label={t("settings.embedding")}
                  value={`${status.data.embeddingProvider} / ${status.data.embeddingModel || "—"} (${status.data.embeddingDimensions}d)`}
                  ok={status.data.embeddingReachable}
                />
                <Row
                  label={t("settings.vectorSearch")}
                  value={status.data.vectorSearchAvailable ? t("settings.available") : t("settings.unavailable")}
                  ok={status.data.vectorSearchAvailable}
                />
                {status.data.detail && <p className="text-meta text-ink-500">{status.data.detail}</p>}
              </>
            )}
          </CardBody>
        </Card>

        {config?.enabled && config.provider === "Local" && <PasswordCard />}
      </div>
    </>
  );
}

function Row({ label, value, ok }: { label: string; value: string; ok: boolean }) {
  const { t } = useI18n();
  return (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <div className="min-w-0">
        <p className="text-meta text-ink-500">{label}</p>
        <p className="truncate font-mono text-ui text-ink-800">{value}</p>
      </div>
      <Badge tone={ok ? "success" : "neutral"}>
        {ok
          ? <CheckCircle2 className="h-3 w-3" aria-hidden />
          : <XCircle className="h-3 w-3" aria-hidden />}
        {ok ? t("settings.reachable") : t("settings.unreachable")}
      </Badge>
    </div>
  );
}

function PasswordCard() {
  const { t } = useI18n();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setDone(false);
    try {
      await api.changePassword(current, next);
      setCurrent("");
      setNext("");
      setDone(true);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t("error.generic"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <CardHeader><CardTitle>{t("settings.password")}</CardTitle></CardHeader>
      <CardBody>
        <form onSubmit={onSubmit} className="space-y-4">
          <Field label={t("settings.currentPassword")} htmlFor="current">
            <Input id="current" type="password" autoComplete="current-password" required
                   value={current} onChange={(event) => setCurrent(event.target.value)} />
          </Field>

          <Field
            label={t("settings.newPassword")}
            htmlFor="next"
            error={error}
            hint="min 12"
          >
            <Input id="next" type="password" autoComplete="new-password" required minLength={12}
                   value={next} onChange={(event) => setNext(event.target.value)} />
          </Field>

          {done && <p className="text-meta text-success-ink">{t("settings.passwordChanged")}</p>}

          <Button type="submit" variant="primary" loading={busy}>{t("action.save")}</Button>
        </form>
      </CardBody>
    </Card>
  );
}
