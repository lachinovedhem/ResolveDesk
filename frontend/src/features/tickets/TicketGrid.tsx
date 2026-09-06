import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { AgGridReact } from "ag-grid-react";
import {
  AllCommunityModule, ModuleRegistry,
  type ColDef, type ICellRendererParams, type RowClickedEvent,
} from "ag-grid-community";
import "ag-grid-community/styles/ag-grid.css";
import "ag-grid-community/styles/ag-theme-quartz.css";

import { Badge } from "@/components/ui/primitives";
import { useI18n } from "@/i18n";
import { useTheme } from "@/lib/theme";
import type { Ticket } from "@/lib/api";
import { PRIORITY_TONE, STATUS_TONE, formatDateTime, isOverdue } from "@/lib/display";

ModuleRegistry.registerModules([AllCommunityModule]);

/**
 * The 1024px-and-up view. Row virtualization carries the whole result set, so there is no pagination
 * to click through: filters live in the header and scrolling is continuous.
 */
export function TicketGrid({ tickets }: { tickets: Ticket[] }) {
  const { t, language } = useI18n();
  const { resolved } = useTheme();
  const navigate = useNavigate();

  const columns = useMemo<ColDef<Ticket>[]>(() => [
    {
      field: "reference",
      headerName: t("ticket.reference"),
      width: 160,
      cellClass: "font-mono text-micro",
    },
    { field: "title", headerName: t("ticket.title"), flex: 2, minWidth: 240 },
    {
      field: "status",
      headerName: t("ticket.status"),
      width: 170,
      cellRenderer: ({ value }: ICellRendererParams<Ticket, Ticket["status"]>) =>
        value ? <Badge tone={STATUS_TONE[value]}>{t(`status.${value}`)}</Badge> : null,
      valueFormatter: ({ value }) => (value ? t(`status.${value as Ticket["status"]}`) : ""),
    },
    {
      field: "priority",
      headerName: t("ticket.priority"),
      width: 140,
      cellRenderer: ({ value }: ICellRendererParams<Ticket, Ticket["priority"]>) =>
        value ? <Badge tone={PRIORITY_TONE[value]}>{t(`priority.${value}`)}</Badge> : null,
      valueFormatter: ({ value }) => (value ? t(`priority.${value as Ticket["priority"]}`) : ""),
    },
    { field: "customerName", headerName: t("ticket.customer"), flex: 1, minWidth: 160 },
    {
      field: "assigneeId",
      headerName: t("ticket.assignee"),
      width: 140,
      valueFormatter: ({ value }) => (value ? `#${value}` : t("ticket.unassigned")),
    },
    {
      field: "slaDueAtUtc",
      headerName: t("ticket.slaDue"),
      width: 190,
      valueFormatter: ({ value }) => formatDateTime(value as string | null, language),
      cellClassRules: {
        // Overdue is worth a colour of its own; everything else stays neutral so it stands out.
        "text-danger-ink font-medium": ({ data }) => !!data && isOverdue(data.slaDueAtUtc, data.status),
      },
    },
    {
      field: "createdAtUtc",
      headerName: t("ticket.created"),
      width: 190,
      valueFormatter: ({ value }) => formatDateTime(value as string, language),
    },
  ], [t, language]);

  const defaultColDef = useMemo<ColDef<Ticket>>(() => ({
    sortable: true,
    resizable: true,
    filter: true,
    // Filters sit under the headers, always visible — no menu to discover first.
    floatingFilter: true,
    suppressHeaderMenuButton: true,
  }), []);

  return (
    <div
      className={`ag-theme-resolvedesk ${resolved === "dark" ? "ag-theme-quartz-dark" : "ag-theme-quartz"} h-full w-full`}
    >
      <AgGridReact<Ticket>
        // `legacy` keeps AG Grid on its CSS-file themes, which is what lets .ag-theme-resolvedesk
        // map the grid's own variables onto our design tokens.
        theme="legacy"
        rowData={tickets}
        columnDefs={columns}
        defaultColDef={defaultColDef}
        getRowId={({ data }) => String(data.id)}
        rowHeight={48}
        headerHeight={44}
        animateRows={false}
        suppressCellFocus
        rowClass="cursor-pointer"
        onRowClicked={({ data }: RowClickedEvent<Ticket>) => data && navigate(`/tickets/${data.id}`)}
      />
    </div>
  );
}
