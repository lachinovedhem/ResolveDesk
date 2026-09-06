import { Suspense, lazy } from "react";
import { BrowserRouter, Navigate, Route, Routes, useLocation } from "react-router-dom";
import { AppLayout } from "./components/AppLayout";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { Skeleton } from "./components/ui/primitives";
import { I18nProvider, useI18n } from "./i18n";
import { AuthProvider, useAuth } from "./lib/auth";
import { NotificationsProvider } from "./lib/notifications";
import { ThemeProvider } from "./lib/theme";
import { DashboardPage } from "./pages/Dashboard";
import { AcceptInvitePage } from "./pages/AcceptInvite";
import { LoginPage } from "./pages/Login";
import { OidcCallbackPage } from "./pages/OidcCallback";

// Split off the routes that pull in AG Grid or the suggestion panel, so the first paint stays small.
const TicketsPage = lazy(() => import("./pages/Tickets").then((m) => ({ default: m.TicketsPage })));
const TicketDetailPage = lazy(() => import("./pages/TicketDetail").then((m) => ({ default: m.TicketDetailPage })));
const NewTicketPage = lazy(() => import("./pages/NewTicket").then((m) => ({ default: m.NewTicketPage })));
const KnowledgePage = lazy(() => import("./pages/Knowledge").then((m) => ({ default: m.KnowledgePage })));
const SettingsPage = lazy(() => import("./pages/Settings").then((m) => ({ default: m.SettingsPage })));

export default function App() {
  return (
    <ThemeProvider>
      <I18nProvider>
        <AuthProvider>
          {/* Inside AuthProvider: the stream only opens once there is a session to stream for. */}
          <NotificationsProvider>
            <BrowserRouter>
              <Shell />
            </BrowserRouter>
          </NotificationsProvider>
        </AuthProvider>
      </I18nProvider>
    </ThemeProvider>
  );
}

function Shell() {
  const { ready, signedIn } = useAuth();
  const { t } = useI18n();
  const location = useLocation();

  // Accepting an invitation happens *before* anyone has a credential, so this route sits outside the
  // gate — sending a new colleague to a sign-in screen they cannot pass would be the whole problem.
  if (location.pathname.startsWith("/invite/") || location.pathname === "/auth/callback") {
    return (
      <Routes>
        <Route path="/invite/:token" element={<AcceptInvitePage />} />
        <Route path="/auth/callback" element={<OidcCallbackPage />} />
      </Routes>
    );
  }

  // Hold the frame until /auth/config answers — rendering the app first and then bouncing to login
  // is a worse experience than a brief, stable skeleton.
  if (!ready) return <BootSkeleton />;
  if (!signedIn) return <LoginPage />;

  return (
    <ErrorBoundary
      resetKey={location.pathname}
      fallbackTitle={t("error.generic")}
      retryLabel={t("action.retry")}
    >
      <Suspense fallback={<BootSkeleton />}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<DashboardPage />} />
            <Route path="tickets" element={<TicketsPage />} />
            <Route path="tickets/new" element={<NewTicketPage />} />
            <Route path="tickets/:id" element={<TicketDetailPage />} />
            <Route path="knowledge" element={<KnowledgePage />} />
            <Route path="settings" element={<SettingsPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </Suspense>
    </ErrorBoundary>
  );
}

function BootSkeleton() {
  return (
    <div className="mx-auto max-w-[1400px] space-y-4 px-4 py-8 sm:px-6 lg:px-10" aria-busy="true">
      <Skeleton className="h-10 w-64" />
      <Skeleton className="h-32 w-full" />
      <Skeleton className="h-64 w-full" />
    </div>
  );
}
