import { Link } from "react-router-dom";
import { AlertTriangle, User } from "lucide-react";
import { Badge } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import type { Ticket } from "@/lib/api";
import { PRIORITY_TONE, STATUS_TONE, formatDuration, isOverdue, isSettled } from "@/lib/display";

/**
 * The below-1024px view. Not a squeezed table: each ticket becomes a card whose hierarchy is
 * title-first, with the columns a phone cannot show folded into a single meta line.
 */
export function TicketTiles({ tickets }: { tickets: Ticket[] }) {
  const { t, language } = useI18n();

  return (
    <ul className="space-y-3">
      {tickets.map((ticket) => {
        const overdue = isOverdue(ticket.slaDueAtUtc, ticket.status);
        return (
          <li key={ticket.id}>
            <Link
              to={`/tickets/${ticket.id}`}
              // 44px minimum touch target, and the whole card is the target.
              className="block min-h-[44px] rounded-card border border-border-subtle bg-surface-2 p-4
                         shadow-soft transition-colors hover:bg-brand-50 focus-visible:outline-none
                         focus-visible:shadow-focus dark:hover:bg-brand-100"
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="truncate text-ui-lg font-medium text-ink-900">{ticket.title}</p>
                  <p className="mt-0.5 font-mono text-micro text-ink-400">{ticket.reference}</p>
                </div>
                <Badge tone={PRIORITY_TONE[ticket.priority]}>{t(`priority.${ticket.priority}`)}</Badge>
              </div>

              <div className="mt-3 flex flex-wrap items-center gap-2">
                <Badge tone={STATUS_TONE[ticket.status]}>{t(`status.${ticket.status}`)}</Badge>
                {overdue && (
                  <Badge tone="danger">
                    <AlertTriangle className="h-3 w-3" aria-hidden />
                    {t("ticket.overdue")}
                  </Badge>
                )}
              </div>

              <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-micro text-ink-500">
                <span className="inline-flex items-center gap-1">
                  <User className="h-3 w-3" aria-hidden />
                  {ticket.customerName}
                </span>
                <span>{t("ticket.assignee")}: {ticket.assigneeId ? `#${ticket.assigneeId}` : t("ticket.unassigned")}</span>
                {ticket.slaDueAtUtc && !isSettled(ticket.status) && (
                  <span>
                    {t(overdue ? "ticket.overdueBy" : "ticket.dueIn",
                       { time: formatDuration(ticket.slaDueAtUtc, language) ?? "" })}
                  </span>
                )}
              </div>
            </Link>
          </li>
        );
      })}
    </ul>
  );
}
