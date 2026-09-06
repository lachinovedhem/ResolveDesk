import { Suspense, lazy, useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Inbox, PlusCircle } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, EmptyState, PageHeader, Select, Skeleton } from "@/components/ui/primitives";
import { TicketTiles } from "@/features/tickets/TicketTiles";
import { useI18n } from "@/i18n";
import { useIsDesktop } from "@/lib/hooks";
import {
  api, TICKET_PRIORITIES, TICKET_STATUSES,
  type Ticket, type TicketPriority, type TicketStatus,
} from "@/lib/api";

const PAGE_SIZE = 50;

// AG Grid and its stylesheet are ~1 MB. Loading them lazily means a phone, which renders tile cards
// instead, never downloads the grid at all.
const TicketGrid = lazy(() => import("@/features/tickets/TicketGrid").then((m) => ({ default: m.TicketGrid })));

/**
 * One data source, two presentations. Above 1024px the rows go into AG Grid (virtualised, filters in
 * the header); below it they become tile cards. The switch is a conditional render, not CSS — a
 * hidden grid would still pay for itself in DOM and memory on a phone.
 *
 * Neither view paginates: "load more" appends to the same virtualised list.
 */
export function TicketsPage() {
  const { t } = useI18n();
  const isDesktop = useIsDesktop();
  const [params, setParams] = useSearchParams();

  const status = (params.get("status") ?? "") as TicketStatus | "";
  const priority = (params.get("priority") ?? "") as TicketPriority | "";
  const search = params.get("search") ?? "";

  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [cursor, setCursor] = useState<number | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<Error | null>(null);

  const load = useCallback(async (append: boolean, from: number | null) => {
    append ? setLoadingMore(true) : setLoading(true);
    setError(null);
    try {
      const page = await api.listTickets({
        status: status || undefined,
        priority: priority || undefined,
        search: search || undefined,
        cursor: from ?? undefined,
        limit: PAGE_SIZE,
      });
      setTickets((previous) => (append ? [...previous, ...page.items] : page.items));
      setCursor(page.nextCursor);
      setHasMore(page.hasMore);
    } catch (caught) {
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    } finally {
      append ? setLoadingMore(false) : setLoading(false);
    }
  }, [status, priority, search]);

  // Filters reset the list rather than appending to a differently-filtered one.
  useEffect(() => { void load(false, null); }, [load]);

  const setFilter = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    value ? next.set(key, value) : next.delete(key);
    setParams(next, { replace: true });
  };

  const hasFilters = Boolean(status || priority || search);

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title={t("nav.tickets")}
        subtitle={search ? `${t("action.search")}: "${search}"` : undefined}
        actions={
          <Link to="/tickets/new">
            <Button variant="primary" icon={<PlusCircle className="h-4 w-4" />}>{t("nav.newTicket")}</Button>
          </Link>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <Select
          aria-label={t("ticket.status")}
          value={status}
          onChange={(event) => setFilter("status", event.target.value)}
          className="w-auto min-w-[10rem]"
        >
          <option value="">{t("ticket.status")}: {t("common.all")}</option>
          {TICKET_STATUSES.map((value) => <option key={value} value={value}>{t(`status.${value}`)}</option>)}
        </Select>

        <Select
          aria-label={t("ticket.priority")}
          value={priority}
          onChange={(event) => setFilter("priority", event.target.value)}
          className="w-auto min-w-[10rem]"
        >
          <option value="">{t("ticket.priority")}: {t("common.all")}</option>
          {TICKET_PRIORITIES.map((value) => <option key={value} value={value}>{t(`priority.${value}`)}</option>)}
        </Select>

        {hasFilters && (
          <Button variant="ghost" size="sm" onClick={() => setParams(new URLSearchParams(), { replace: true })}>
            {t("action.clearFilters")}
          </Button>
        )}
      </div>

      {loading && <ListSkeleton />}

      {!loading && error && (
        <Card className="p-6">
          <p className="text-ui text-danger-ink" role="alert">{error.message}</p>
          <Button className="mt-4" onClick={() => void load(false, null)}>{t("action.retry")}</Button>
        </Card>
      )}

      {!loading && !error && tickets.length === 0 && (
        <Card>
          <EmptyState
            icon={<Inbox className="h-10 w-10" aria-hidden />}
            title={t("empty.tickets")}
            action={hasFilters
              ? <Button onClick={() => setParams(new URLSearchParams(), { replace: true })}>{t("action.clearFilters")}</Button>
              : <Link to="/tickets/new"><Button variant="primary">{t("nav.newTicket")}</Button></Link>}
          />
        </Card>
      )}

      {!loading && !error && tickets.length > 0 && (
        <>
          <div className="min-h-0 flex-1">
            {isDesktop
              ? <Suspense fallback={<ListSkeleton />}><TicketGrid tickets={tickets} /></Suspense>
              : <TicketTiles tickets={tickets} />}
          </div>

          {hasMore && (
            <div className="mt-4 flex justify-center">
              <Button loading={loadingMore} onClick={() => void load(true, cursor)}>
                {t("action.loadMore")}
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function ListSkeleton() {
  return (
    <div className="space-y-3" aria-busy="true">
      {Array.from({ length: 6 }, (_, row) => <Skeleton key={row} className="h-20 w-full" />)}
    </div>
  );
}
