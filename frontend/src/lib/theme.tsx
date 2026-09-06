import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

export type ThemeMode = "light" | "dark" | "system";

const STORAGE_KEY = "resolvedesk.theme";

interface ThemeContextValue {
  mode: ThemeMode;
  /** What is actually on screen right now — `system` resolves to one of these. */
  resolved: "light" | "dark";
  setMode: (mode: ThemeMode) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

function read(): ThemeMode {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === "light" || stored === "dark" || stored === "system" ? stored : "system";
  } catch {
    return "system";
  }
}

/** Matches --surface-canvas in each theme; the browser chrome should continue the page, not fight it. */
const CHROME_COLOR = { light: "#f4f6f6", dark: "#0e1212" } as const;

/**
 * Keeps the browser's own chrome — address bar, status bar, PWA title bar — in step with the theme.
 *
 * index.html ships two media-scoped `theme-color` tags so the first paint is right, but those follow
 * the *operating system*. Once someone picks a theme in the app the two can disagree, and the result
 * is a dark bar above a light page. On an explicit choice the tags are replaced by a single fixed one;
 * back on "system", the media-scoped pair is restored so the OS decides again.
 */
function applyChromeColour(mode: ThemeMode, resolved: "light" | "dark") {
  const head = document.head;
  for (const meta of head.querySelectorAll('meta[name="theme-color"]')) meta.remove();

  const add = (content: string, media?: string) => {
    const meta = document.createElement("meta");
    meta.name = "theme-color";
    meta.content = content;
    if (media) meta.media = media;
    head.appendChild(meta);
  };

  if (mode === "system") {
    add(CHROME_COLOR.light, "(prefers-color-scheme: light)");
    add(CHROME_COLOR.dark, "(prefers-color-scheme: dark)");
  } else {
    add(CHROME_COLOR[resolved]);
  }
}

/** `.dark` goes on <html>, not <body>, so portalled modals and dropdowns inherit the theme too. */
function apply(mode: ThemeMode): "light" | "dark" {
  const dark = mode === "dark" ||
    (mode === "system" && window.matchMedia("(prefers-color-scheme: dark)").matches);
  document.documentElement.classList.toggle("dark", dark);
  document.documentElement.style.colorScheme = dark ? "dark" : "light";
  const resolved = dark ? "dark" : "light";
  applyChromeColour(mode, resolved);
  return resolved;
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setModeState] = useState<ThemeMode>(read);
  const [resolved, setResolved] = useState<"light" | "dark">(() => apply(read()));

  const setMode = useCallback((next: ThemeMode) => {
    setModeState(next);
    try { localStorage.setItem(STORAGE_KEY, next); } catch { /* private mode */ }
    setResolved(apply(next));
  }, []);

  // In `system` mode the OS can change under us — follow it live rather than only at load.
  useEffect(() => {
    if (mode !== "system") return;
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => setResolved(apply("system"));
    media.addEventListener("change", onChange);
    return () => media.removeEventListener("change", onChange);
  }, [mode]);

  const value = useMemo(() => ({ mode, resolved, setMode }), [mode, resolved, setMode]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) throw new Error("useTheme must be used inside ThemeProvider.");
  return context;
}
