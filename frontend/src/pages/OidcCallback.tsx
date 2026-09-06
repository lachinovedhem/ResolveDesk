import { useEffect, useRef, useState } from "react";
import { AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, Skeleton } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import { api, tokenStore } from "@/lib/api";

/** The coarse reasons the callback can report. Deliberately not specific — see OidcEndpoints. */
const REASONS = {
  denied: "sso.denied",
  invalid: "sso.invalid",
  rejected: "sso.rejected",
  unavailable: "sso.unavailable",
} as const;

/**
 * Captured when this module first loads, before any effect can clear the address bar.
 *
 * Reading the query inside the effect looked fine and was not: StrictMode runs effects twice in
 * development, the first run strips the code out of the URL, and the second run then reads an empty
 * query and reports "that link was incomplete" over a sign-in that had already succeeded.
 */
const initialQuery = typeof window === "undefined" ? "" : window.location.search;

/**
 * Where the identity provider's redirect finally lands.
 *
 * The URL carries a one-time code rather than a session token, so this page's only job is to trade it
 * in and then get rid of it. The code is single-use, so the exchange must fire exactly once however
 * many times the effect runs — hence the ref as well as the captured query.
 */
export function OidcCallbackPage() {
  const { t } = useI18n();
  const [error, setError] = useState<string | null>(null);

  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;

    const params = new URLSearchParams(initialQuery);
    const failure = params.get("error");
    const code = params.get("code");

    // Whatever happens, the code should not stay in the address bar or the history entry.
    window.history.replaceState({}, "", "/");

    if (failure) {
      setError(REASONS[failure as keyof typeof REASONS] ?? "sso.rejected");
      return;
    }
    if (!code) {
      setError("sso.invalid");
      return;
    }

    api.exchangeOidcCode(code)
      .then((session) => {
        tokenStore.set(session.token);
        try {
          localStorage.setItem("resolvedesk.session", JSON.stringify({
            userId: session.userId, fullName: session.fullName,
            email: session.email, role: session.role,
          }));
        } catch { /* private mode */ }
        // A full load, so every provider re-reads the session that now exists.
        window.location.assign("/");
      })
      .catch(() => setError("sso.rejected"));
  }, []);

  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-12">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-3 text-center">
          <img src="/brand/logo.svg" alt="" className="h-14 w-14" />
          <h1 className="brand-word text-title font-semibold text-ink-900">{t("app.name")}</h1>
        </div>

        <Card>
          <CardBody className="space-y-3">
            {!error && (
              <>
                <p className="text-ui text-ink-700">{t("sso.completing")}</p>
                <Skeleton className="h-10 w-full" />
              </>
            )}

            {error && (
              <div className="space-y-3 text-center">
                <AlertTriangle className="mx-auto h-8 w-8 text-warning" aria-hidden />
                <p className="text-ui font-medium text-ink-900">{t("sso.failed")}</p>
                <p className="text-meta text-ink-500">{t(error as "sso.rejected")}</p>
                <Button variant="ghost" className="w-full" onClick={() => window.location.assign("/")}>
                  {t("invite.toSignIn")}
                </Button>
              </div>
            )}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}
