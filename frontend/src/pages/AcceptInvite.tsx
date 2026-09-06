import { useEffect, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { CheckCircle2, Link2Off } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, Field, Input, Skeleton } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import { api, tokenStore, type InvitationPeek } from "@/lib/api";

const MINIMUM_LENGTH = 12;

/**
 * Where a setup link lands. Reached without a session — the whole point is that the person has no
 * credential yet — so this route sits outside the authentication gate.
 *
 * The link is single-use, and a used one is indistinguishable from one that never existed. That is
 * deliberate: a forwarded email should not tell whoever receives it that it was once real.
 */
export function AcceptInvitePage() {
  const { t } = useI18n();
  const navigate = useNavigate();
  const { token = "" } = useParams();

  const [invitation, setInvitation] = useState<InvitationPeek | null>(null);
  const [loading, setLoading] = useState(true);
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.peekInvite(token)
      .then(setInvitation)
      .catch(() => setInvitation(null))
      .finally(() => setLoading(false));
  }, [token]);

  const tooShort = password.length > 0 && password.length < MINIMUM_LENGTH;
  const mismatch = confirm.length > 0 && confirm !== password;
  const ready = password.length >= MINIMUM_LENGTH && confirm === password;

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const session = await api.acceptInvite(token, password);
      tokenStore.set(session.token);
      try {
        localStorage.setItem("resolvedesk.session", JSON.stringify({
          userId: session.userId, fullName: session.fullName,
          email: session.email, role: session.role,
        }));
      } catch { /* private mode */ }
      // A full load rather than a client-side navigation, so every provider re-reads the new session.
      window.location.assign("/");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t("error.generic"));
      setBusy(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-12">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-3 text-center">
          <img src="/brand/logo.svg" alt="" className="h-14 w-14" />
          <h1 className="brand-word text-title font-semibold text-ink-900">{t("app.name")}</h1>
        </div>

        <Card>
          <CardBody className="space-y-4">
            {loading && <Skeleton className="h-32 w-full" />}

            {!loading && !invitation && (
              <div className="space-y-3 text-center">
                <Link2Off className="mx-auto h-8 w-8 text-ink-400" aria-hidden />
                <p className="text-ui font-medium text-ink-900">{t("invite.deadTitle")}</p>
                <p className="text-meta text-ink-500">{t("invite.deadBody")}</p>
                <Button variant="ghost" className="w-full" onClick={() => navigate("/")}>
                  {t("invite.toSignIn")}
                </Button>
              </div>
            )}

            {!loading && invitation && (
              <form onSubmit={onSubmit} className="space-y-4">
                <div className="flex items-start gap-2">
                  <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-brand-500" aria-hidden />
                  <div>
                    <p className="text-ui font-medium text-ink-900">{invitation.fullName}</p>
                    <p className="text-meta text-ink-500">{invitation.email}</p>
                  </div>
                </div>
                <p className="text-meta text-ink-500">{t("invite.chooseHint", { minimum: MINIMUM_LENGTH })}</p>

                <Field
                  label={t("invite.password")}
                  htmlFor="new-password"
                  error={tooShort ? t("invite.tooShort", { minimum: MINIMUM_LENGTH }) : undefined}
                >
                  <Input
                    id="new-password"
                    type="password"
                    autoComplete="new-password"
                    autoFocus
                    required
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                  />
                </Field>

                <Field
                  label={t("invite.confirm")}
                  htmlFor="confirm-password"
                  error={mismatch ? t("invite.mismatch") : error ?? undefined}
                >
                  <Input
                    id="confirm-password"
                    type="password"
                    autoComplete="new-password"
                    required
                    value={confirm}
                    onChange={(event) => setConfirm(event.target.value)}
                  />
                </Field>

                <Button type="submit" variant="primary" className="w-full" loading={busy} disabled={!ready}>
                  {t("invite.submit")}
                </Button>
              </form>
            )}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}
