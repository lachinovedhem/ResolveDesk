import type { HTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from "react";
import { cn } from "@/lib/cn";

/* ── Card ─────────────────────────────────────────────────────────────────────────────────────── */

/** Opaque on purpose: text contrast must not depend on the decorative canvas behind it. */
export function Card({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("rounded-card bg-surface-2 border border-border-subtle shadow-soft", className)} {...rest} />;
}

export function CardHeader({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("px-6 py-4 border-b border-border-subtle", className)} {...rest} />;
}

export function CardTitle({ className, ...rest }: HTMLAttributes<HTMLHeadingElement>) {
  return <h2 className={cn("text-title-sm font-semibold text-ink-900", className)} {...rest} />;
}

export function CardBody({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("px-6 py-4", className)} {...rest} />;
}

/* ── Badge ────────────────────────────────────────────────────────────────────────────────────── */

export type BadgeTone =
  | "open" | "progress" | "done"
  | "info" | "success" | "warning" | "danger" | "accent" | "neutral";

const TONES: Record<BadgeTone, string> = {
  open: "bg-status-open-soft text-status-open-ink",
  progress: "bg-status-progress-soft text-status-progress-ink",
  done: "bg-status-done-soft text-status-done-ink",
  info: "bg-info-soft text-info-ink",
  success: "bg-success-soft text-success-ink",
  warning: "bg-warning-soft text-warning-ink",
  danger: "bg-danger-soft text-danger-ink",
  accent: "bg-accent-soft text-accent-ink",
  neutral: "bg-ink-100 text-ink-700",
};

export function Badge({ tone = "neutral", className, ...rest }: HTMLAttributes<HTMLSpanElement> & { tone?: BadgeTone }) {
  return (
    <span
      className={cn("inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-micro font-medium", TONES[tone], className)}
      {...rest}
    />
  );
}

/* ── Form controls ────────────────────────────────────────────────────────────────────────────── */

const CONTROL =
  "w-full rounded-control bg-surface-2 border border-border-subtle px-3 text-ui text-ink-900 " +
  "placeholder:text-ink-400 transition-colors focus-visible:outline-none focus-visible:shadow-focus " +
  "focus-visible:border-brand-500 disabled:opacity-50";

export function Input({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cn(CONTROL, "h-11", className)} {...rest} />;
}

export function Textarea({ className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className={cn(CONTROL, "py-2 min-h-[7rem] resize-y", className)} {...rest} />;
}

export function Select({ className, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className={cn(CONTROL, "h-11 pr-8", className)} {...rest} />;
}

/** Label, control and error as one unit, so the label is always tied to its input. */
export function Field({ label, htmlFor, error, hint, children }: {
  label: string; htmlFor: string; error?: string | null; hint?: string; children: ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={htmlFor} className="block text-meta font-medium text-ink-600">{label}</label>
      {children}
      {hint && !error && <p className="text-micro text-ink-400">{hint}</p>}
      {error && <p className="text-micro text-danger-ink" role="alert">{error}</p>}
    </div>
  );
}

/* ── State placeholders ───────────────────────────────────────────────────────────────────────── */

/** Shape-preserving placeholder: layout does not jump when the real content arrives. */
export function Skeleton({ className }: { className?: string }) {
  return <div className={cn("animate-pulse rounded-control bg-ink-200/70", className)} aria-hidden />;
}

export function EmptyState({ icon, title, description, action }: {
  icon?: ReactNode; title: string; description?: string; action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 px-6 py-16 text-center">
      {icon && <div className="text-ink-300">{icon}</div>}
      <p className="text-ui-lg font-medium text-ink-800">{title}</p>
      {description && <p className="max-w-md text-ui text-ink-500">{description}</p>}
      {action}
    </div>
  );
}

export function PageHeader({ title, subtitle, actions }: {
  title: string; subtitle?: string; actions?: ReactNode;
}) {
  return (
    <header className="mb-6 flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="text-title font-semibold text-ink-900">{title}</h1>
        {subtitle && <p className="mt-1 text-ui text-ink-500">{subtitle}</p>}
      </div>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </header>
  );
}
