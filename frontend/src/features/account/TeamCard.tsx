import { useEffect, useState } from "react";
import { Check, Copy, Link2, UserPlus } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Badge, Card, CardBody, CardHeader, CardTitle, Field, Input, Skeleton } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import { api, type Invitation, type UserRole } from "@/lib/api";
import { formatDuration, isPast } from "@/lib/display";

const ROLES: UserRole[] = ["Agent", "Coordinator", "Admin"];

/**
 * Where accounts come from. There is no sign-up form anywhere in this product: someone who already
 * has an account creates the next one, and the new person receives a link rather than a password
 * that a colleague chose and sent in plain text.
 *
 * The link is shown exactly once. The server stores only its hash, so this card cannot fetch it back
 * later — which is why the copy button is prominent and the warning is not subtle.
 */
export function TeamCard() {
  const { t, language } = useI18n();
  const [pending, setPending] = useState<Invitation[] | null>(null);
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ fullName: "", email: "", role: "Agent" as UserRole, skills: "" });
  const [issued, setIssued] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = () => { void api.invitations().then(setPending).catch(() => setPending([])); };
  useEffect(refresh, []);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const created = await api.invite({
        fullName: form.fullName.trim(),
        email: form.email.trim(),
        role: form.role,
        skills: form.skills.trim() || null,
      });
      setIssued(created.url ?? null);
      setForm({ fullName: "", email: "", role: "Agent", skills: "" });
      setOpen(false);
      refresh();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : t("error.generic"));
    } finally {
      setBusy(false);
    }
  };

  const copy = async (url: string) => {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard access can be refused; the link is on screen and selectable either way.
    }
  };

  const ready = form.fullName.trim().length > 1 && form.email.includes("@");

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <UserPlus className="h-4 w-4 text-brand-500" aria-hidden />
          {t("team.title")}
        </CardTitle>
        <p className="mt-1 text-meta text-ink-500">{t("team.subtitle")}</p>
      </CardHeader>

      <CardBody className="space-y-3">
        {issued && (
          <div className="space-y-2 rounded-control border border-brand-200 bg-brand-50 p-3">
            <p className="flex items-center gap-2 text-ui font-medium text-ink-900">
              <Link2 className="h-4 w-4 text-brand-600" aria-hidden />
              {t("team.linkTitle")}
            </p>
            <p className="text-micro text-ink-600">{t("team.linkHint")}</p>
            <code className="block break-all rounded-control border border-border-subtle bg-surface-2 p-2
                             font-mono text-micro text-ink-800">
              {issued}
            </code>
            <div className="flex gap-2">
              <Button variant="primary" size="sm" onClick={() => void copy(issued)}>
                {copied ? <Check className="h-3.5 w-3.5" aria-hidden /> : <Copy className="h-3.5 w-3.5" aria-hidden />}
                {copied ? t("team.copied") : t("team.copy")}
              </Button>
              <Button variant="ghost" size="sm" onClick={() => setIssued(null)}>
                {t("action.close")}
              </Button>
            </div>
          </div>
        )}

        {pending === null && <Skeleton className="h-16 w-full" />}

        {pending?.map((invitation) => {
          const expired = isPast(invitation.expiresAtUtc);
          return (
            <div key={invitation.userId}
                 className="flex flex-wrap items-center justify-between gap-2 rounded-control
                            border border-border-subtle bg-surface-1 p-3">
              <div className="min-w-0">
                <p className="truncate text-ui text-ink-900">{invitation.fullName}</p>
                <p className="truncate text-micro text-ink-500">{invitation.email}</p>
              </div>
              <div className="flex items-center gap-2">
                <Badge tone={expired ? "danger" : "neutral"}>
                  {expired
                    ? t("team.expired")
                    : t("team.expires", { time: formatDuration(invitation.expiresAtUtc, language) ?? "" })}
                </Badge>
                <Button variant="ghost" size="sm"
                        onClick={() => void api.resendInvite(invitation.userId)
                          .then((fresh) => { setIssued(fresh.url ?? null); refresh(); })}>
                  {t("team.resend")}
                </Button>
                <Button variant="ghost" size="sm"
                        onClick={() => void api.revokeInvite(invitation.userId).then(refresh)}>
                  {t("team.revoke")}
                </Button>
              </div>
            </div>
          );
        })}

        {!open && (
          <Button variant="secondary" size="sm" onClick={() => setOpen(true)}>
            {t("team.invite")}
          </Button>
        )}

        {open && (
          <div className="space-y-3">
            <Field label={t("team.fullName")} htmlFor="invite-name">
              <Input id="invite-name" autoFocus value={form.fullName}
                     onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
            </Field>
            <Field label={t("team.email")} htmlFor="invite-email" error={error ?? undefined}>
              <Input id="invite-email" type="email" autoComplete="off" value={form.email}
                     onChange={(e) => setForm({ ...form, email: e.target.value })} />
            </Field>
            <Field label={t("team.role")} htmlFor="invite-role">
              <select
                id="invite-role"
                className="min-h-[44px] w-full rounded-control border border-border-subtle bg-surface-2
                           px-3 text-ui text-ink-900"
                value={form.role}
                onChange={(e) => setForm({ ...form, role: e.target.value as UserRole })}
              >
                {ROLES.map((role) => <option key={role} value={role}>{t(`role.${role}`)}</option>)}
              </select>
            </Field>
            <Field label={t("team.skills")} htmlFor="invite-skills">
              <Input id="invite-skills" value={form.skills} placeholder="IPTV, DSL"
                     onChange={(e) => setForm({ ...form, skills: e.target.value })} />
            </Field>
            <div className="flex gap-2">
              <Button variant="primary" size="sm" loading={busy} disabled={!ready} onClick={submit}>
                {t("team.send")}
              </Button>
              <Button variant="ghost" size="sm" onClick={() => { setOpen(false); setError(null); }}>
                {t("action.cancel")}
              </Button>
            </div>
          </div>
        )}
      </CardBody>
    </Card>
  );
}
