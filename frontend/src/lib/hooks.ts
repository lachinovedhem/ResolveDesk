import { useCallback, useEffect, useState } from "react";

/**
 * True at or above 1024px — the line where the data view switches from tile cards to AG Grid.
 * Driven by matchMedia rather than a resize listener so it does not fire on every pixel.
 */
export function useIsDesktop() {
  const [isDesktop, setIsDesktop] = useState(() =>
    typeof window !== "undefined" && window.matchMedia("(min-width: 1024px)").matches);

  useEffect(() => {
    const media = window.matchMedia("(min-width: 1024px)");
    const onChange = (event: MediaQueryListEvent) => setIsDesktop(event.matches);
    media.addEventListener("change", onChange);
    setIsDesktop(media.matches);
    return () => media.removeEventListener("change", onChange);
  }, []);

  return isDesktop;
}

/** Delays a fast-changing value — used so typing does not fire a request per keystroke. */
export function useDebounced<T>(value: T, delayMs = 300) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
}

interface AsyncState<T> { data: T | null; error: Error | null; loading: boolean }

/**
 * Minimal data fetching: run an async function, track its state, ignore results that arrive after
 * a newer call has already started.
 */
export function useAsync<T>(factory: () => Promise<T>, deps: unknown[]): AsyncState<T> & { reload: () => void } {
  const [state, setState] = useState<AsyncState<T>>({ data: null, error: null, loading: true });
  const [nonce, setNonce] = useState(0);
  const reload = useCallback(() => setNonce((n) => n + 1), []);

  useEffect(() => {
    let current = true;
    setState((previous) => ({ ...previous, loading: true, error: null }));
    factory()
      .then((data) => { if (current) setState({ data, error: null, loading: false }); })
      .catch((error: Error) => { if (current) setState({ data: null, error, loading: false }); });
    return () => { current = false; };
    // The caller owns the dependency list; `factory` is intentionally not part of it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce]);

  return { ...state, reload };
}

/** Ctrl/Cmd+K, ignored while the user is typing in a field or a dialog is open. */
export function useCommandKey(onTrigger: () => void) {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.code !== "KeyK" || !(event.ctrlKey || event.metaKey)) return;
      const target = event.target as HTMLElement | null;
      const tag = target?.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || target?.isContentEditable) return;
      if (document.querySelector("[role='dialog']")) return;
      event.preventDefault();
      onTrigger();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onTrigger]);
}
