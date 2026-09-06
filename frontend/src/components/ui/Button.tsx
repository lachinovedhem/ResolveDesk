import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from "react";
import { Loader2 } from "lucide-react";
import { cn } from "@/lib/cn";

type Variant = "primary" | "secondary" | "ghost" | "danger";
type Size = "sm" | "md" | "lg";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  loading?: boolean;
  icon?: ReactNode;
}

// Only colour transitions: animating layout properties is what makes a UI feel like it stutters.
const BASE =
  "inline-flex items-center justify-center gap-2 rounded-control font-medium " +
  "transition-colors duration-150 disabled:opacity-50 disabled:pointer-events-none " +
  "focus-visible:outline-none focus-visible:shadow-focus";

const VARIANTS: Record<Variant, string> = {
  primary: "bg-brand text-brand-on-primary shadow-brand hover:brightness-110",
  secondary:
    "bg-surface-2 text-ink-800 border border-border-subtle hover:bg-brand-50 " +
    "dark:hover:bg-brand-100",
  ghost: "text-ink-700 hover:bg-brand-50 dark:hover:bg-brand-100",
  danger: "bg-danger text-brand-on-primary hover:brightness-110",
};

// Every height is a multiple of 8, and the smallest is still a 44px touch target on mobile.
const SIZES: Record<Size, string> = {
  sm: "h-9 px-3 text-meta min-h-[44px] sm:min-h-0",
  md: "h-11 px-4 text-ui",
  lg: "h-12 px-6 text-ui-lg",
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = "secondary", size = "md", loading = false, icon, className, children, disabled, ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      disabled={disabled || loading}
      className={cn(BASE, VARIANTS[variant], SIZES[size], className)}
      {...rest}
    >
      {loading ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : icon}
      {children}
    </button>
  );
});
