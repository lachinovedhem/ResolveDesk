import { useEffect, useState } from "react";
import { Fingerprint, KeyRound, ShieldCheck, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Badge, Card, CardBody, CardHeader, CardTitle, Field, Input, Skeleton } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import { api, type Passkey, type TotpStatus } from "@/lib/api";
import { formatDuration } from "@/lib/display";
import { createPasskey, passkeysSupported, platformAuthenticatorAvailable, wasCancelled } from "@/lib/webauthn";

/**
 * The two credentials a person manages for themselves. Both are additions to a password rather than
 * replacements chosen for them, which is why each one explains what it does before asking for
 * anything — someone who does not understand a second factor is someone who will lock themselves out.
 */
export function PasskeyCard() {
  const { t, language } = useI18n();
  const [passkeys, setPasskeys] = useState<Passkey[] | null>(null);
  const [platform, setPlatform] = useState(false);
  const [label, setLabel] = useState("");
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const supported = passkeysSupported();

  const refresh = () => { void api.passkeys().then(setPasskeys).catch(() => setPasskeys([])); };
  useEffect(() => { refresh(); void platformAuthenticatorAvailable().then(setPlatform); }, []);

  const register = async () => {
    setBusy(true);
    setError(null);
    try {
      const challenge = await api.passkeyRegisterBegin();
      const credential = await createPasskey(challenge.optionsJson);
      await api.passkeyRegisterFinish(challenge.challengeId, label.trim() || "Passkey", credential);
      setLabel("");
      setAdding(false);
      refresh();
    } catch (caught) {
      if (!wasCancelled(caught)) setError(caught instanceof Error ? caught.message : t("error.generic"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          {platform ? <Fingerprint className="h-4 w-4 text-brand-500" aria-hidden />
                    : <KeyRound className="h-4 w-4 text-brand-500" aria-hidden />}
          {t("security.passkeys")}
        </CardTitle>
        <p className="mt-1 text-meta text-ink-500">{t("security.passkeysHint")}</p>
      </CardHeader>

      <CardBody className="space-y-3">
        {!supported && <p className="text-meta text-ink-500">{t("security.unsupported")}</p>}

        {supported && passkeys === null && <Skeleton className="h-16 w-full" />}

        {supported && passkeys?.length === 0 && !adding && (
          <p className="text-meta text-ink-500">{t("security.noPasskeys")}</p>
        )}

        {passkeys?.map((passkey) => (
          <div key={passkey.id}
               className="flex items-center justify-between gap-3 rounded-control border border-border-subtle bg-surface-1 p-3">
            <div className="min-w-0">
              <p className="truncate text-ui text-ink-900">{passkey.label}</p>
              <p className="text-micro text-ink-500">
                {passkey.lastUsedAtUtc
                  ? t("security.lastUsed", { time: formatDuration(passkey.lastUsedAtUtc, language) ?? "" })
                  : t("security.neverUsed")}
              </p>
            </div>
            <Button variant="ghost" size="sm" aria-label={t("action.delete")}
                    onClick={() => void api.deletePasskey(passkey.id).then(refresh)}>
              <Trash2 className="h-4 w-4" aria-hidden />
            </Button>
          </div>
        ))}

        {supported && !adding && (
          <Button variant="secondary" size="sm" onClick={() => setAdding(true)}>
            {t("security.addPasskey")}
          </Button>
        )}

        {supported && adding && (
          <div className="space-y-3">
            <Field label={t("security.passkeyLabel")} htmlFor="passkey-label" error={error ?? undefined}>
              <Input id="passkey-label" autoFocus maxLength={60} value={label}
                     placeholder={platform ? "MacBook" : "YubiKey"}
                     onChange={(event) => setLabel(event.target.value)} />
            </Field>
            <div className="flex gap-2">
              <Button variant="primary" size="sm" loading={busy} onClick={register}>
                {t("security.addPasskey")}
              </Button>
              <Button variant="ghost" size="sm" onClick={() => { setAdding(false); setError(null); }}>
                {t("action.cancel")}
              </Button>
            </div>
          </div>
        )}
      </CardBody>
    </Card>
  );
}

export function TwoFactorCard() {
  const { t } = useI18n();
  const [status, setStatus] = useState<TotpStatus | null>(null);
  const [setup, setSetup] = useState<{ secret: string; otpAuthUri: string } | null>(null);
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = () => { void api.totpStatus().then(setStatus).catch(() => setStatus(null)); };
  useEffect(refresh, []);

  const begin = async () => {
    setBusy(true); setError(null);
    try { setSetup(await api.totpSetup()); }
    catch (caught) { setError(caught instanceof Error ? caught.message : t("error.generic")); }
    finally { setBusy(false); }
  };

  const submit = async (action: "enable" | "disable") => {
    setBusy(true); setError(null);
    try {
      await (action === "enable" ? api.totpEnable(code) : api.totpDisable(code));
      setSetup(null); setCode(""); refresh();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t("error.generic"));
    } finally { setBusy(false); }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <ShieldCheck className="h-4 w-4 text-brand-500" aria-hidden />
          {t("security.totp")}
          {status && (
            <Badge tone={status.enrolled ? "done" : "neutral"}>
              {status.enrolled ? t("security.totpOn") : t("security.totpOff")}
            </Badge>
          )}
        </CardTitle>
        <p className="mt-1 text-meta text-ink-500">{t("security.totpHint")}</p>
      </CardHeader>

      <CardBody className="space-y-3">
        {status === null && <Skeleton className="h-16 w-full" />}

        {status && !status.available && (
          <p className="text-meta text-ink-500">{t("security.totpUnavailable")}</p>
        )}

        {status?.available && !status.enrolled && !setup && (
          <Button variant="secondary" size="sm" loading={busy} onClick={begin}>
            {t("security.enable")}
          </Button>
        )}

        {setup && (
          <div className="space-y-3">
            <p className="text-meta text-ink-600">{t("security.scan")}</p>
            {/* The secret in plain text as well as the URI: not every authenticator scans a link,
                and a person retyping 32 characters should not have to dig it out of a QR code. */}
            <code className="block break-all rounded-control border border-border-subtle bg-surface-1 p-3
                             font-mono text-micro text-ink-800">
              {setup.secret}
            </code>
            <a className="text-meta text-brand-600 underline" href={setup.otpAuthUri}>
              {setup.otpAuthUri.slice(0, 48)}…
            </a>

            <Field label={t("security.confirmCode")} htmlFor="totp-code" error={error ?? undefined}>
              <Input id="totp-code" inputMode="numeric" maxLength={6} autoComplete="one-time-code"
                     className="text-center font-mono tracking-[0.4em]"
                     value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))} />
            </Field>
            <div className="flex gap-2">
              <Button variant="primary" size="sm" loading={busy} disabled={code.length !== 6}
                      onClick={() => submit("enable")}>
                {t("security.enable")}
              </Button>
              <Button variant="ghost" size="sm" onClick={() => { setSetup(null); setCode(""); setError(null); }}>
                {t("action.cancel")}
              </Button>
            </div>
          </div>
        )}

        {status?.enrolled && !setup && (
          <div className="space-y-3">
            {/* Turning it off needs a current code, so a borrowed session cannot strip the factor. */}
            <Field label={t("security.confirmCode")} htmlFor="totp-off" error={error ?? undefined}>
              <Input id="totp-off" inputMode="numeric" maxLength={6} autoComplete="one-time-code"
                     className="text-center font-mono tracking-[0.4em]"
                     value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))} />
            </Field>
            <Button variant="danger" size="sm" loading={busy} disabled={code.length !== 6}
                    onClick={() => submit("disable")}>
              {t("security.disable")}
            </Button>
          </div>
        )}
      </CardBody>
    </Card>
  );
}
