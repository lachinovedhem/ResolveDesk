import type { Config } from "tailwindcss";

/**
 * Every colour is a `<alpha-value>` wrapper around a CSS variable from src/styles/tokens.css.
 * That is what makes `bg-brand-500/20` work while keeping tokens.css the only place a literal
 * colour is ever written.
 */
const rgb = (name: string) => `rgb(var(--${name}) / <alpha-value>)`;

const ramp = (prefix: string, steps: readonly (string | number)[]) =>
  Object.fromEntries(steps.map((s) => [String(s), rgb(`${prefix}-${s}`)]));

export default {
  darkMode: "class",
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        brand: {
          ...ramp("brand", [50, 100, 200, 300, 400, 500, 600, 700, 800, 900]),
          "on-primary": rgb("brand-on-primary"),
        },
        ink: ramp("ink", [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950]),
        surface: {
          canvas: rgb("surface-canvas"),
          1: rgb("surface-1"),
          2: rgb("surface-2"),
          3: rgb("surface-3"),
        },
        border: { subtle: rgb("border-subtle"), strong: rgb("border-strong") },
        status: {
          open: rgb("status-open"),
          "open-soft": rgb("status-open-soft"),
          "open-ink": rgb("status-open-ink"),
          progress: rgb("status-progress"),
          "progress-soft": rgb("status-progress-soft"),
          "progress-ink": rgb("status-progress-ink"),
          done: rgb("status-done"),
          "done-soft": rgb("status-done-soft"),
          "done-ink": rgb("status-done-ink"),
        },
        info: rgb("info"), "info-soft": rgb("info-soft"), "info-ink": rgb("info-ink"),
        success: rgb("success"), "success-soft": rgb("success-soft"), "success-ink": rgb("success-ink"),
        warning: rgb("warning"), "warning-soft": rgb("warning-soft"), "warning-ink": rgb("warning-ink"),
        danger: rgb("danger"), "danger-soft": rgb("danger-soft"), "danger-ink": rgb("danger-ink"),
        accent: rgb("accent"), "accent-soft": rgb("accent-soft"), "accent-ink": rgb("accent-ink"),
      },
      fontFamily: {
        ui: "var(--font-ui)",
        brand: "var(--font-brand)",
        mono: "var(--font-mono)",
      },
      fontSize: {
        micro: ["var(--text-micro)", { lineHeight: "1.4" }],
        meta: ["var(--text-meta)", { lineHeight: "1.45" }],
        ui: ["var(--text-ui)", { lineHeight: "1.5" }],
        "ui-lg": ["var(--text-ui-lg)", { lineHeight: "1.5" }],
        "title-sm": ["var(--text-title-sm)", { lineHeight: "1.35", letterSpacing: "-0.015em" }],
        title: ["var(--text-title)", { lineHeight: "1.25", letterSpacing: "-0.015em" }],
      },
      borderRadius: { card: "var(--radius-card)", control: "var(--radius-control)" },
      boxShadow: {
        soft: "var(--shadow-soft)",
        elevated: "var(--shadow-elevated)",
        focus: "var(--shadow-focus)",
        brand: "var(--shadow-brand)",
      },
      backgroundImage: { brand: "var(--brand-gradient)" },
      // 8pt grid: the defaults are already multiples of 4, these fill the gaps we actually use.
      spacing: { 18: "4.5rem", 76: "19rem", 260: "16.25rem" },
    },
  },
  plugins: [],
} satisfies Config;
