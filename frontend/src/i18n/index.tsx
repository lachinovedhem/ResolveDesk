import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { az } from "./az";
import { en } from "./en";

export type Language = "az" | "en";
export type TranslationKey = keyof typeof az;

const DICTIONARIES: Record<Language, Record<TranslationKey, string>> = { az, en };
const STORAGE_KEY = "resolvedesk.lang";

interface I18nContextValue {
  language: Language;
  setLanguage: (language: Language) => void;
  /** `{name}` placeholders are filled from `values`. */
  t: (key: TranslationKey, values?: Record<string, string | number>) => string;
}

const I18nContext = createContext<I18nContextValue | null>(null);

function read(): Language {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "az" || stored === "en") return stored;
    return navigator.language.startsWith("az") ? "az" : "en";
  } catch {
    return "az";
  }
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(read);

  const setLanguage = useCallback((next: Language) => {
    setLanguageState(next);
    document.documentElement.lang = next;
    try { localStorage.setItem(STORAGE_KEY, next); } catch { /* private mode */ }
  }, []);

  const t = useCallback(
    (key: TranslationKey, values?: Record<string, string | number>) => {
      // English is the fallback for any key a translation has not caught up with yet.
      const template = DICTIONARIES[language][key] ?? en[key] ?? key;
      if (!values) return template;
      return template.replace(/\{(\w+)\}/g, (match, name: string) =>
        name in values ? String(values[name]) : match);
    },
    [language],
  );

  const value = useMemo(() => ({ language, setLanguage, t }), [language, setLanguage, t]);
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  const context = useContext(I18nContext);
  if (!context) throw new Error("useI18n must be used inside I18nProvider.");
  return context;
}
