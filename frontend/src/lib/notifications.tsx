import {
  createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode,
} from "react";
import { api, tokenStore, type AppNotification } from "./api";
import { useAuth } from "./auth";

interface NotificationsContextValue {
  items: AppNotification[];
  unreadCount: number;
  /** True while the live stream is connected; false means the bell only updates on reload. */
  live: boolean;
  markRead: (id: number) => Promise<void>;
  markAllRead: () => Promise<void>;
  refresh: () => Promise<void>;
  /** Ask the browser for permission to show desktop notifications. Must be called from a click. */
  requestPermission: () => Promise<void>;
  permission: NotificationPermission | "unsupported";
}

const NotificationsContext = createContext<NotificationsContextValue | null>(null);

export function NotificationsProvider({ children }: { children: ReactNode }) {
  const { session, signedIn, config } = useAuth();
  const [items, setItems] = useState<AppNotification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [live, setLive] = useState(false);
  const [permission, setPermission] = useState<NotificationPermission | "unsupported">(
    typeof Notification === "undefined" ? "unsupported" : Notification.permission);

  // With auth off there is no identity, so the API needs to be told whose bell this is.
  const userId = config?.enabled ? undefined : session?.userId ?? 1;

  const refresh = useCallback(async () => {
    if (!signedIn) return;
    try {
      const page = await api.notifications(userId, false, 50);
      setItems(page.items);
      setUnreadCount(page.unreadCount);
    } catch {
      // The bell is not worth an error banner; it will catch up on the next refresh.
    }
  }, [signedIn, userId]);

  useEffect(() => { void refresh(); }, [refresh]);

  const show = useCallback((notification: AppNotification) => {
    setItems((previous) => [notification, ...previous.filter((n) => n.id !== notification.id)].slice(0, 50));
    setUnreadCount((count) => count + 1);

    // A PWA in the background is the case this exists for. Only ever after an explicit grant.
    if (typeof Notification !== "undefined" && Notification.permission === "granted" && document.hidden) {
      try {
        new Notification(notification.title, { body: notification.body, tag: `resolvedesk-${notification.id}` });
      } catch {
        // Some browsers only allow this from a service worker; the in-app bell still updated.
      }
    }
  }, []);

  useLiveStream({ enabled: signedIn, userId, onNotification: show, onLiveChange: setLive });

  // The app-icon badge is what makes an installed PWA useful when it is not on screen.
  useEffect(() => {
    const navigatorWithBadge = navigator as Navigator & {
      setAppBadge?: (count?: number) => Promise<void>;
      clearAppBadge?: () => Promise<void>;
    };
    if (unreadCount > 0) void navigatorWithBadge.setAppBadge?.(unreadCount).catch(() => {});
    else void navigatorWithBadge.clearAppBadge?.().catch(() => {});
  }, [unreadCount]);

  const markRead = useCallback(async (id: number) => {
    setItems((previous) => previous.map((n) => (n.id === id ? { ...n, readAtUtc: new Date().toISOString() } : n)));
    setUnreadCount((count) => Math.max(0, count - 1));
    try { await api.markNotificationRead(id, userId); } catch { void refresh(); }
  }, [userId, refresh]);

  const markAllRead = useCallback(async () => {
    const now = new Date().toISOString();
    setItems((previous) => previous.map((n) => (n.readAtUtc ? n : { ...n, readAtUtc: now })));
    setUnreadCount(0);
    try { await api.markAllNotificationsRead(userId); } catch { void refresh(); }
  }, [userId, refresh]);

  const requestPermission = useCallback(async () => {
    if (typeof Notification === "undefined") return;
    setPermission(await Notification.requestPermission());
  }, []);

  const value = useMemo(() => ({
    items, unreadCount, live, markRead, markAllRead, refresh, requestPermission, permission,
  }), [items, unreadCount, live, markRead, markAllRead, refresh, requestPermission, permission]);

  return <NotificationsContext.Provider value={value}>{children}</NotificationsContext.Provider>;
}

/**
 * Subscribes to the server-sent event stream.
 *
 * Uses fetch + ReadableStream rather than EventSource, because EventSource cannot send an
 * Authorization header — the alternative would be putting the access token in the URL, where it ends
 * up in proxy logs and browser history. Reconnection is therefore ours to handle, with a capped
 * backoff so a restarting API does not get hammered.
 */
function useLiveStream({ enabled, userId, onNotification, onLiveChange }: {
  enabled: boolean;
  userId?: number;
  onNotification: (notification: AppNotification) => void;
  onLiveChange: (live: boolean) => void;
}) {
  // Kept in a ref so a re-render with a new callback identity does not tear down the stream.
  const handler = useRef(onNotification);
  handler.current = onNotification;

  useEffect(() => {
    if (!enabled) return;

    const controller = new AbortController();
    let attempt = 0;
    let stopped = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;

    const connect = async () => {
      const token = tokenStore.get();
      const query = userId === undefined ? "" : `?userId=${userId}`;

      try {
        const response = await fetch(`/api/v1/notifications/stream${query}`, {
          headers: {
            Accept: "text/event-stream",
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
          },
          signal: controller.signal,
        });

        if (!response.ok || !response.body) throw new Error(`stream ${response.status}`);

        attempt = 0;
        onLiveChange(true);

        const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
        let buffer = "";

        for (;;) {
          const { value, done } = await reader.read();
          if (done) break;
          buffer += value;

          // SSE frames are separated by a blank line.
          let split: number;
          while ((split = buffer.indexOf("\n\n")) >= 0) {
            const frame = buffer.slice(0, split);
            buffer = buffer.slice(split + 2);

            const data = frame
              .split("\n")
              .filter((line) => line.startsWith("data:"))
              .map((line) => line.slice(5).trim())
              .join("\n");

            if (!data) continue;
            try { handler.current(JSON.parse(data) as AppNotification); } catch { /* partial frame */ }
          }
        }
      } catch {
        // Network blip, API restart, or the tab going away — all handled the same way.
      }

      onLiveChange(false);
      if (stopped || controller.signal.aborted) return;

      attempt += 1;
      const delay = Math.min(30_000, 1000 * 2 ** Math.min(attempt, 5));
      retryTimer = setTimeout(connect, delay);
    };

    void connect();

    return () => {
      stopped = true;
      clearTimeout(retryTimer);
      controller.abort();
      onLiveChange(false);
    };
  }, [enabled, userId, onLiveChange]);
}

export function useNotifications() {
  const context = useContext(NotificationsContext);
  if (!context) throw new Error("useNotifications must be used inside NotificationsProvider.");
  return context;
}
