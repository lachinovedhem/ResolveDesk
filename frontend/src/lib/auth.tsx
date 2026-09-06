import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import {
  api, setUnauthorizedHandler, tokenStore,
  type AuthConfig, type LoginResponse, type UserRole,
} from "./api";
import { getPasskeyAssertion } from "./webauthn";

interface Session { userId: number; fullName: string; email: string; role: UserRole }

interface AuthContextValue {
  /** Null until the API has told us whether authentication is on at all. */
  config: AuthConfig | null;
  session: Session | null;
  /** True once the shell may render: either signed in, or auth is switched off. */
  ready: boolean;
  signedIn: boolean;
  /**
   * Resolves to "mfa" when the password was right but the account has a second factor. The caller
   * shows the code field; nothing is stored until `completeMfa` succeeds, so an abandoned attempt
   * leaves no half-signed-in state behind.
   */
  signIn: (username: string, password: string) => Promise<"done" | "mfa">;
  completeMfa: (code: string) => Promise<void>;
  signInWithPasskey: () => Promise<void>;
  signOut: () => void;
}

const SESSION_KEY = "resolvedesk.session";
const AuthContext = createContext<AuthContextValue | null>(null);

function readSession(): Session | null {
  try {
    const raw = localStorage.getItem(SESSION_KEY);
    return raw ? (JSON.parse(raw) as Session) : null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [config, setConfig] = useState<AuthConfig | null>(null);
  const [session, setSession] = useState<Session | null>(() => (tokenStore.get() ? readSession() : null));
  const [pendingMfa, setPendingMfa] = useState<string | null>(null);

  const signOut = useCallback(() => {
    setPendingMfa(null);
    tokenStore.clear();
    try { localStorage.removeItem(SESSION_KEY); } catch { /* private mode */ }
    setSession(null);
  }, []);

  // Any 401 anywhere in the app drops the session — no stale "signed in" shell over a dead token.
  useEffect(() => { setUnauthorizedHandler(signOut); }, [signOut]);

  useEffect(() => {
    api.authConfig()
      .then(setConfig)
      // If even the config call fails the API is unreachable; assume auth is on and let the login
      // screen surface the real error rather than silently opening the app.
      .catch(() => setConfig({
        enabled: true, provider: "Local", acceptsPassword: true,
        methods: ["Password"], passkeysEnabled: false, totpAvailable: false, totpRequired: false,
        oidcInteractive: false,
      }));
  }, []);

  const establish = useCallback((result: LoginResponse) => {
    tokenStore.set(result.token);
    const next: Session = {
      userId: result.userId, fullName: result.fullName, email: result.email, role: result.role,
    };
    try { localStorage.setItem(SESSION_KEY, JSON.stringify(next)); } catch { /* private mode */ }
    setSession(next);
  }, []);

  const signIn = useCallback(async (username: string, password: string) => {
    const result = await api.login(username, password);
    if ("mfaRequired" in result) {
      // Held in memory only, and only until the code is entered — it is a step in one sign-in, not
      // a credential worth persisting anywhere a later page load could find it.
      setPendingMfa(result.mfaToken);
      return "mfa" as const;
    }
    establish(result);
    return "done" as const;
  }, [establish]);

  const completeMfa = useCallback(async (code: string) => {
    if (!pendingMfa) throw new Error("There is no sign-in waiting for a code.");
    establish(await api.verifyMfa(pendingMfa, code));
    setPendingMfa(null);
  }, [pendingMfa, establish]);

  const signInWithPasskey = useCallback(async () => {
    const challenge = await api.passkeyLoginBegin();
    const credential = await getPasskeyAssertion(challenge.optionsJson);
    establish(await api.passkeyLoginFinish(challenge.challengeId, credential));
  }, [establish]);

  const value = useMemo<AuthContextValue>(() => ({
    config,
    session,
    ready: config !== null,
    signedIn: config !== null && (!config.enabled || session !== null),
    signIn,
    completeMfa,
    signInWithPasskey,
    signOut,
  }), [config, session, signIn, completeMfa, signInWithPasskey, signOut]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used inside AuthProvider.");
  return context;
}
