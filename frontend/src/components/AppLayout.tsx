import { useCallback, useEffect, useRef, useState } from "react";
import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  BookOpen, ChevronLeft, ChevronRight, Download, LayoutDashboard, LogOut,
  Menu, Monitor, Moon, PanelsTopLeft, PlusCircle, Search, Settings, Sun, X,
} from "lucide-react";
import { Button } from "./ui/Button";
import { NotificationBell } from "./NotificationBell";
import { cn } from "@/lib/cn";
import { useI18n, type Language } from "@/i18n";
import { useAuth } from "@/lib/auth";
import { useTheme, type ThemeMode } from "@/lib/theme";
import { useCommandKey } from "@/lib/hooks";
import type { TranslationKey } from "@/i18n";

const SIDEBAR_KEY = "resolvedesk.sidebar";

const NAV: { to: string; labelKey: TranslationKey; icon: typeof LayoutDashboard }[] = [
  { to: "/", labelKey: "nav.dashboard", icon: LayoutDashboard },
  { to: "/tickets", labelKey: "nav.tickets", icon: PanelsTopLeft },
  { to: "/tickets/new", labelKey: "nav.newTicket", icon: PlusCircle },
  { to: "/knowledge", labelKey: "nav.knowledge", icon: BookOpen },
  { to: "/settings", labelKey: "nav.settings", icon: Settings },
];

/**
 * Fixed-height shell: the page column scrolls, the frame does not. That is what keeps the header and
 * the AG Grid header row anchored while a long ticket list scrolls underneath.
 */
export function AppLayout() {
  const { t } = useI18n();
  const location = useLocation();
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(() => {
    try { return localStorage.getItem(SIDEBAR_KEY) === "1"; } catch { return false; }
  });
  const [drawerOpen, setDrawerOpen] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);

  const toggleCollapsed = useCallback(() => {
    setCollapsed((previous) => {
      const next = !previous;
      try { localStorage.setItem(SIDEBAR_KEY, next ? "1" : "0"); } catch { /* private mode */ }
      return next;
    });
  }, []);

  useCommandKey(() => searchRef.current?.focus());

  // A route change should never leave the mobile drawer covering the page it navigated to.
  useEffect(() => { setDrawerOpen(false); }, [location.pathname]);

  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar collapsed={collapsed} onToggle={toggleCollapsed} className="hidden lg:flex" />

      {drawerOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-ink-950/40 backdrop-blur-sm" onClick={() => setDrawerOpen(false)} aria-hidden />
          <Sidebar collapsed={false} onToggle={() => setDrawerOpen(false)} className="relative z-10 flex h-full" mobile />
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header
          className="flex h-14 shrink-0 items-center gap-3 border-b border-border-subtle
                     bg-surface-3/85 px-4 backdrop-blur-md sm:px-6 lg:h-16 lg:px-10"
        >
          <button
            onClick={() => setDrawerOpen(true)}
            className="rounded-control p-2 text-ink-600 transition-colors hover:bg-brand-50 lg:hidden"
            aria-label={t("nav.menu")}
          >
            <Menu className="h-5 w-5" />
          </button>

          <Link to="/" className="flex items-center gap-2 lg:hidden">
            <img src="/brand/logo.svg" alt="" className="h-8 w-8" />
            <span className="brand-word text-ui-lg font-semibold text-ink-900">{t("app.name")}</span>
          </Link>

          <form
            className="ml-auto hidden max-w-md flex-1 lg:ml-0 lg:block"
            onSubmit={(event) => {
              event.preventDefault();
              const value = searchRef.current?.value.trim();
              navigate(value ? `/tickets?search=${encodeURIComponent(value)}` : "/tickets");
            }}
          >
            <label className="relative block">
              <span className="sr-only">{t("action.search")}</span>
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" aria-hidden />
              <input
                ref={searchRef}
                type="search"
                placeholder={t("action.searchPlaceholder")}
                className="h-10 w-full rounded-full border border-border-subtle bg-surface-2 pl-9 pr-16
                           text-ui text-ink-900 placeholder:text-ink-400 transition-colors
                           focus-visible:border-brand-500 focus-visible:outline-none focus-visible:shadow-focus"
              />
              <kbd className="pointer-events-none absolute right-3 top-1/2 hidden -translate-y-1/2
                              rounded border border-border-subtle px-1.5 py-0.5 font-mono text-micro
                              text-ink-400 xl:block">
                Ctrl K
              </kbd>
            </label>
          </form>

          <div className="ml-auto flex items-center gap-1">
            <NotificationBell />
            <HeaderActions />
          </div>
        </header>

        <main className="scrollbar-thin flex-1 overflow-y-auto">
          {/* Bottom padding on mobile clears the tab bar and the home-indicator inset. */}
          <div className="mx-auto h-full max-w-[1400px] px-4 py-6 pb-24 sm:px-6 lg:px-10 lg:pb-6">
            <Outlet />
          </div>
        </main>

        <MobileTabBar />
      </div>
    </div>
  );
}

/**
 * Thumb-reachable navigation below 1024px. The sidebar drawer still exists for the full list; this
 * covers the four things an agent does on a phone without opening anything.
 */
function MobileTabBar() {
  const { t } = useI18n();
  const tabs = NAV.filter((item) => item.to !== "/settings");

  return (
    <nav
      className="fixed inset-x-0 bottom-0 z-30 flex border-t border-border-subtle bg-surface-3/95
                 backdrop-blur-md lg:hidden"
      // Keeps the bar above the iOS home indicator instead of under it.
      style={{ paddingBottom: "env(safe-area-inset-bottom)" }}
      aria-label={t("nav.menu")}
    >
      {tabs.map(({ to, labelKey, icon: Icon }) => (
        <NavLink
          key={to}
          to={to}
          end={to === "/" || to === "/tickets"}
          className={({ isActive }) => cn(
            "flex min-h-[56px] flex-1 flex-col items-center justify-center gap-0.5 px-1 py-2",
            "text-micro transition-colors",
            isActive ? "text-brand-600" : "text-ink-500",
          )}
        >
          <Icon className="h-5 w-5" aria-hidden />
          <span className="truncate">{t(labelKey)}</span>
        </NavLink>
      ))}
    </nav>
  );
}

function Sidebar({ collapsed, onToggle, className, mobile = false }: {
  collapsed: boolean; onToggle: () => void; className?: string; mobile?: boolean;
}) {
  const { t } = useI18n();
  const { session, signOut, config } = useAuth();

  return (
    <aside
      className={cn(
        "flex-col border-r border-border-subtle bg-surface-3",
        collapsed ? "w-[76px]" : "w-[260px]",
        className,
      )}
    >
      <div className="flex h-14 items-center gap-2 px-4 lg:h-16">
        <img src="/brand/logo.svg" alt="" className="h-9 w-9 shrink-0" />
        {!collapsed && (
          <span className="brand-word truncate text-ui-lg font-semibold text-ink-900">{t("app.name")}</span>
        )}
        <button
          onClick={onToggle}
          className="ml-auto rounded-control p-2 text-ink-500 transition-colors hover:bg-brand-50 hover:text-ink-800"
          aria-label={mobile ? t("action.cancel") : collapsed ? t("nav.expand") : t("nav.collapse")}
        >
          {mobile ? <X className="h-4 w-4" /> : collapsed ? <ChevronRight className="h-4 w-4" /> : <ChevronLeft className="h-4 w-4" />}
        </button>
      </div>

      <nav className="scrollbar-thin flex-1 space-y-1 overflow-y-auto px-3 py-4">
        {NAV.map(({ to, labelKey, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            end={to === "/" || to === "/tickets"}
            className={({ isActive }) => cn(
              "flex min-h-[44px] items-center gap-3 rounded-control px-3 py-2 text-ui transition-colors",
              isActive
                ? "bg-brand text-brand-on-primary shadow-brand"
                : "text-ink-700 hover:bg-brand-50 dark:hover:bg-brand-100",
            )}
            title={collapsed ? t(labelKey) : undefined}
          >
            <Icon className="h-5 w-5 shrink-0" aria-hidden />
            {!collapsed && <span className="truncate">{t(labelKey)}</span>}
          </NavLink>
        ))}
      </nav>

      {session && config?.enabled && (
        <div className="border-t border-border-subtle p-3">
          {!collapsed && (
            <div className="mb-2 px-2">
              <p className="truncate text-meta font-medium text-ink-800">{session.fullName}</p>
              <p className="truncate text-micro text-ink-400">{t(`role.${session.role}`)}</p>
            </div>
          )}
          <Button variant="ghost" size="sm" onClick={signOut} className="w-full justify-start"
                  icon={<LogOut className="h-4 w-4" />}>
            {!collapsed && t("nav.signOut")}
          </Button>
        </div>
      )}
    </aside>
  );
}

/** Theme, language and the PWA install prompt, grouped into one pill. */
function HeaderActions() {
  const { t, language, setLanguage } = useI18n();
  const { mode, setMode } = useTheme();
  const [installPrompt, setInstallPrompt] = useState<Event | null>(null);

  useEffect(() => {
    const onBeforeInstall = (event: Event) => { event.preventDefault(); setInstallPrompt(event); };
    window.addEventListener("beforeinstallprompt", onBeforeInstall);
    return () => window.removeEventListener("beforeinstallprompt", onBeforeInstall);
  }, []);

  const install = async () => {
    const prompt = installPrompt as (Event & { prompt: () => Promise<void>; userChoice: Promise<unknown> }) | null;
    if (!prompt) return;
    await prompt.prompt();
    await prompt.userChoice;
    setInstallPrompt(null);
  };

  const themeIcon = mode === "dark" ? Moon : mode === "light" ? Sun : Monitor;
  const nextMode: Record<ThemeMode, ThemeMode> = { light: "dark", dark: "system", system: "light" };
  const ThemeIcon = themeIcon;

  return (
    <div className="flex items-center gap-1 rounded-full border border-border-subtle bg-surface-2 p-1">
      {installPrompt && (
        <button onClick={install}
                className="rounded-full p-2 text-ink-600 transition-colors hover:bg-brand-50 hover:text-ink-900"
                aria-label={t("action.install")} title={t("action.install")}>
          <Download className="h-4 w-4" />
        </button>
      )}
      <button
        onClick={() => setLanguage(language === "az" ? "en" : ("az" as Language))}
        className="rounded-full px-3 py-1.5 text-meta font-medium uppercase text-ink-600
                   transition-colors hover:bg-brand-50 hover:text-ink-900"
        aria-label={t("lang.label")}
      >
        {language}
      </button>
      <button
        onClick={() => setMode(nextMode[mode])}
        className="rounded-full p-2 text-ink-600 transition-colors hover:bg-brand-50 hover:text-ink-900"
        aria-label={`${t("theme.label")}: ${t(`theme.${mode}`)}`}
        title={t(`theme.${mode}`)}
      >
        <ThemeIcon className="h-4 w-4" />
      </button>
    </div>
  );
}
