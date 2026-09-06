import { useEffect, useState, type FormEvent } from "react";
import { Fingerprint, KeyRound, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardBody, Field, Input } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { passkeysSupported, platformAuthenticatorAvailable, wasCancelled } from "@/lib/webauthn";

/**
 * One screen for every method the deployment has turned on, side by side rather than one instead of
 * another: a team can run corporate SSO for staff, a password for a contractor, and passkeys for
 * whoever has enrolled one, and this screen simply shows what /auth/config reports.
 *
 * The second-factor step is a separate view of the same screen. Until the code is accepted there is
 * no token and no session — abandoning it halfway leaves nothing behind.
 */
export function LoginPage() {
  const { t } = useI18n();
  const { config, signIn, completeMfa, signInWithPasskey } = useAuth();

  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [stage, setStage] = useState<"credentials" | "mfa">("credentials");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<"password" | "passkey" | "mfa" | null>(null);
  const [platformAuth, setPlatformAuth] = useState(false);

  useEffect(() => { void platformAuthenticatorAvailable().then(setPlatformAuth); }, []);

  const methods = config?.methods ?? ["Password"];
  const showPassword = methods.includes("Password") && config?.acceptsPassword !== false;
  const showOidc = methods.includes("Oidc");
  // Offered only when the browser can actually do it — a button that always fails is worse than none.
  const showPasskey = methods.includes("Passkey") && passkeysSupported();

  const onPassword = async (event: FormEvent) => {
    event.preventDefault();
    setBusy("password");
    setError(null);
    try {
      if (await signIn(username, password) === "mfa") {
        setStage("mfa");
        setCode("");
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t("error.generic"));
    } finally {
      setBusy(null);
    }
  };

  const onMfa = async (event: FormEvent) => {
    event.preventDefault();
    setBusy("mfa");
    setError(null);
    try {
      await completeMfa(code);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t("error.generic"));
    } finally {
      setBusy(null);
    }
  };

  const onPasskey = async () => {
    setBusy("passkey");
    setError(null);
    try {
      await signInWithPasskey();
    } catch (caught) {
      // Dismissing the system prompt is a decision, not a failure; saying nothing is the right reply.
      if (!wasCancelled(caught)) {
        setError(caught instanceof Error ? caught.message : t("error.generic"));
      }
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center px-4 py-12">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center gap-3 text-center">
          <img src="/brand/logo.svg" alt="" className="h-14 w-14" />
          <h1 className="brand-word text-title font-semibold text-ink-900">{t("app.name")}</h1>
          <p className="text-meta text-ink-500">{t("app.tagline")}</p>
        </div>

        <Card>
          <CardBody className="space-y-4">
            {stage === "mfa" ? (
              <form onSubmit={onMfa} className="space-y-4">
                <div className="flex items-center gap-2 text-ui text-ink-800">
                  <ShieldCheck className="h-4 w-4 text-brand-500" aria-hidden />
                  {t("login.mfaTitle")}
                </div>
                <p className="text-meta text-ink-500">{t("login.mfaHint")}</p>

                <Field label={t("login.code")} htmlFor="code" error={error}>
                  <Input
                    id="code"
                    // A phone offers to fill this in when the field says what it is.
                    autoComplete="one-time-code"
                    inputMode="numeric"
                    pattern="[0-9]*"
                    maxLength={6}
                    autoFocus
                    required
                    className="text-center font-mono text-title-sm tracking-[0.4em]"
                    value={code}
                    onChange={(event) => setCode(event.target.value.replace(/\D/g, ""))}
                  />
                </Field>

                <Button type="submit" variant="primary" className="w-full"
                        loading={busy === "mfa"} disabled={code.length !== 6}>
                  {t("login.verify")}
                </Button>
                <Button type="button" variant="ghost" className="w-full"
                        onClick={() => { setStage("credentials"); setError(null); setPassword(""); }}>
                  {t("action.back")}
                </Button>
              </form>
            ) : (
              <>
                {showPassword && (
                  <form onSubmit={onPassword} className="space-y-4">
                    <Field label={t("login.username")} htmlFor="username">
                      <Input
                        id="username"
                        autoComplete="username webauthn"
                        autoFocus
                        required
                        value={username}
                        onChange={(event) => setUsername(event.target.value)}
                      />
                    </Field>

                    <Field label={t("login.password")} htmlFor="password" error={error}>
                      <Input
                        id="password"
                        type="password"
                        autoComplete="current-password"
                        required
                        value={password}
                        onChange={(event) => setPassword(event.target.value)}
                      />
                    </Field>

                    <Button type="submit" variant="primary" className="w-full" loading={busy === "password"}>
                      {t("login.submit")}
                    </Button>
                  </form>
                )}

                {showPassword && (showPasskey || showOidc) && (
                  <div className="flex items-center gap-3 py-1">
                    <span className="h-px flex-1 bg-border-subtle" />
                    <span className="text-micro uppercase tracking-wide text-ink-400">{t("login.or")}</span>
                    <span className="h-px flex-1 bg-border-subtle" />
                  </div>
                )}

                {showPasskey && (
                  <Button type="button" variant="secondary" className="w-full"
                          loading={busy === "passkey"} onClick={onPasskey}>
                    {platformAuth
                      ? <Fingerprint className="h-4 w-4" aria-hidden />
                      : <KeyRound className="h-4 w-4" aria-hidden />}
                    {platformAuth ? t("login.biometric") : t("login.securityKey")}
                  </Button>
                )}

                {showOidc && config?.oidcInteractive && (
                  // A full navigation, not fetch: the provider will redirect the browser several
                  // times and set its own cookies, none of which survives an XHR.
                  <Button type="button" variant="secondary" className="w-full"
                          onClick={() => { window.location.href = "/api/v1/auth/oidc/start"; }}>
                    {t("login.sso")}
                  </Button>
                )}

                {/* Passthrough deployments have no route to send anyone to; say so instead of
                    offering a button that goes nowhere. */}
                {showOidc && !config?.oidcInteractive && (
                  <p className="text-micro text-ink-400">{t("login.oidc")}</p>
                )}

                {!showPassword && !showPasskey && !showOidc && (
                  <p className="text-ui text-ink-600">{t("login.noMethods")}</p>
                )}

                {/* A passkey failure has no field of its own to hang an error on. */}
                {error && !showPassword && <p className="text-meta text-danger">{error}</p>}
              </>
            )}
          </CardBody>
        </Card>
      </div>
    </div>
  );
}
