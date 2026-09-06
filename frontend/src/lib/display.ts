import type { BadgeTone } from "@/components/ui/primitives";
import type { TicketPriority, TicketStatus } from "./api";

/**
 * Status is stage, so it takes cool hues; priority is urgency, so it takes warm ones. Keeping the two
 * scales apart is what lets a glance at a row separate "where is this" from "how bad is this".
 */
export const STATUS_TONE: Record<TicketStatus, BadgeTone> = {
  Open: "open",
  Assigned: "open",
  InProgress: "progress",
  WaitingCustomer: "progress",
  Resolved: "done",
  Closed: "done",
};

export const PRIORITY_TONE: Record<TicketPriority, BadgeTone> = {
  Low: "neutral",
  Normal: "info",
  High: "warning",
  Urgent: "danger",
};

/** Locale-aware date and time; the API always sends UTC and the browser renders it locally. */
export function formatDateTime(iso: string | null | undefined, locale: string) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString(locale, {
    year: "numeric", month: "short", day: "2-digit", hour: "2-digit", minute: "2-digit",
  });
}

export function formatDate(iso: string | null | undefined, locale: string) {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString(locale, { year: "numeric", month: "short", day: "2-digit" });
}

/**
 * A bare duration — "3 days", "3 gün" — with no preposition and no direction.
 *
 * Intl.RelativeTimeFormat is deliberately not used. Chromium ships no relative-time data for `az`, so
 * it degrades to "+7 h" and "-3 d": untranslated, and unreadable next to the rest of the page. It
 * also returns a whole phrase ("in 7 hours"), which cannot be composed into a sentence that reads
 * correctly in both languages — Azerbaijani puts the direction *after* the duration. Returning only
 * the quantity lets each locale's own string supply the framing.
 */
export function formatDuration(iso: string | null | undefined, locale: string) {
  if (!iso) return null;

  const deltaMs = Math.abs(new Date(iso).getTime() - Date.now());
  const minutes = Math.round(deltaMs / 60_000);
  const hours = Math.round(minutes / 60);

  const [value, unit] =
    minutes < 60 ? [Math.max(minutes, 1), "minute" as const]
    : hours < 24 ? [hours, "hour" as const]
    : [Math.round(hours / 24), "day" as const];

  if (locale.startsWith("az")) {
    // Azerbaijani nouns are not pluralised after a numeral: "7 saat", not "7 saatlar".
    return `${value} ${{ minute: "dəqiqə", hour: "saat", day: "gün" }[unit]}`;
  }
  return `${value} ${unit}${value === 1 ? "" : "s"}`;
}

/** True when the instant is in the past — the caller picks the wording from this. */
export function isPast(iso: string | null | undefined) {
  return iso ? new Date(iso).getTime() < Date.now() : false;
}

/** Work is finished, so its deadline has stopped meaning anything. */
export function isSettled(status: TicketStatus) {
  return status === "Resolved" || status === "Closed";
}

export function isOverdue(slaDueAtUtc: string | null | undefined, status: TicketStatus) {
  if (!slaDueAtUtc || isSettled(status)) return false;
  return new Date(slaDueAtUtc).getTime() < Date.now();
}
