import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Bell, BellRing, Check, Radio } from "lucide-react";
import { cn } from "@/lib/cn";
import { useI18n } from "@/i18n";
import { useNotifications } from "@/lib/notifications";
import { formatDuration } from "@/lib/display";
import type { NotificationKind } from "@/lib/api";

// Cool hues for progress-type events, warm ones only for things that need action.
const TONE: Record<NotificationKind, string> = {
  Assigned: "bg-status-open-soft text-status-open-ink",
  StatusChanged: "bg-status-progress-soft text-status-progress-ink",
  Comment: "bg-ink-100 text-ink-700",
  SlaRisk: "bg-danger-soft text-danger-ink",
  ResolutionReviewed: "bg-warning-soft text-warning-ink",
  DuplicateDetected: "bg-accent-soft text-accent-ink",
};

export function NotificationBell() {
  const { t, language } = useI18n();
  const { items, unreadCount, live, markRead, markAllRead, requestPermission, permission } = useNotifications();
  const [open, setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: MouseEvent) => {
      if (!container.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const Icon = unreadCount > 0 ? BellRing : Bell;

  return (
    <div ref={container} className="relative">
      <button
        onClick={() => setOpen((previous) => !previous)}
        className="relative rounded-full p-2 text-ink-600 transition-colors hover:bg-brand-50 hover:text-ink-900"
        aria-label={t("notifications.title")}
        aria-expanded={open}
      >
        <Icon className="h-4 w-4" />
        {unreadCount > 0 && (
          <span
            className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full
                       bg-danger px-1 text-micro font-semibold text-brand-on-primary"
            aria-label={t("notifications.unread", { count: unreadCount })}
          >
            {unreadCount > 9 ? "9+" : unreadCount}
          </span>
        )}
      </button>

      {open && (
        <div
          className="absolute right-0 top-full z-40 mt-2 w-[min(22rem,calc(100vw-2rem))] overflow-hidden
                     rounded-card border border-border-subtle bg-surface-2 shadow-elevated"
          role="dialog"
          aria-label={t("notifications.title")}
        >
          <div className="flex items-center justify-between border-b border-border-subtle px-4 py-3">
            <div className="flex items-center gap-2">
              <h2 className="text-ui font-medium text-ink-900">{t("notifications.title")}</h2>
              {/* Honest about whether the list is live or only refreshed on load. */}
              <span
                className={cn("inline-flex items-center gap-1 text-micro",
                  live ? "text-success-ink" : "text-ink-400")}
                title={live ? t("notifications.live") : t("notifications.offline")}
              >
                <Radio className="h-3 w-3" aria-hidden />
                {live ? t("notifications.live") : t("notifications.offline")}
              </span>
            </div>
            {unreadCount > 0 && (
              <button
                onClick={() => void markAllRead()}
                className="inline-flex items-center gap-1 text-micro text-brand-600 transition-colors hover:text-brand-700"
              >
                <Check className="h-3 w-3" aria-hidden />
                {t("notifications.markAllRead")}
              </button>
            )}
          </div>

          {permission === "default" && (
            <button
              onClick={() => void requestPermission()}
              className="w-full border-b border-border-subtle bg-brand-50 px-4 py-2 text-left text-micro
                         text-ink-700 transition-colors hover:bg-brand-100"
            >
              {t("notifications.enableDesktop")}
            </button>
          )}

          <ul className="scrollbar-thin max-h-[60vh] divide-y divide-border-subtle overflow-y-auto">
            {items.length === 0 && (
              <li className="px-4 py-8 text-center text-meta text-ink-400">{t("notifications.empty")}</li>
            )}

            {items.map((notification) => {
              const body = (
                <>
                  <div className="flex items-start justify-between gap-2">
                    <span className={cn("rounded-full px-2 py-0.5 text-micro font-medium", TONE[notification.kind])}>
                      {t(`notifications.kind.${notification.kind}`)}
                    </span>
                    <span className="shrink-0 text-micro text-ink-400">
                      {t("time.ago", { time: formatDuration(notification.createdAtUtc, language) ?? "" })}
                    </span>
                  </div>
                  <p className="mt-1 text-meta font-medium text-ink-900">{notification.title}</p>
                  {notification.body && (
                    <p className="mt-0.5 line-clamp-2 text-micro text-ink-500">{notification.body}</p>
                  )}
                </>
              );

              const className = cn(
                "block min-h-[44px] w-full px-4 py-3 text-left transition-colors hover:bg-brand-50 dark:hover:bg-brand-100",
                !notification.readAtUtc && "bg-brand-50/60 dark:bg-brand-100/40",
              );

              return (
                <li key={notification.id}>
                  {notification.ticketId ? (
                    <Link
                      to={`/tickets/${notification.ticketId}`}
                      className={className}
                      onClick={() => {
                        if (!notification.readAtUtc) void markRead(notification.id);
                        setOpen(false);
                      }}
                    >
                      {body}
                    </Link>
                  ) : (
                    <button
                      className={className}
                      onClick={() => !notification.readAtUtc && void markRead(notification.id)}
                    >
                      {body}
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
