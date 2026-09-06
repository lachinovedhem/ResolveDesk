import { Component, type ErrorInfo, type ReactNode } from "react";
import { AlertTriangle } from "lucide-react";

interface Props { children: ReactNode; resetKey?: string; fallbackTitle: string; retryLabel: string }
interface State { error: Error | null }

/**
 * Keeps one broken page from blanking the whole shell. `resetKey` is the pathname, so navigating away
 * from a failed route clears the error instead of stranding the user on it.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidUpdate(previous: Props) {
    if (previous.resetKey !== this.props.resetKey && this.state.error) {
      this.setState({ error: null });
    }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("Unhandled error in route", error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;

    return (
      <div className="flex flex-col items-center justify-center gap-4 px-6 py-20 text-center">
        <AlertTriangle className="h-10 w-10 text-warning" aria-hidden />
        <p className="text-ui-lg font-medium text-ink-900">{this.props.fallbackTitle}</p>
        <p className="max-w-md text-meta text-ink-500">{this.state.error.message}</p>
        <button
          onClick={() => this.setState({ error: null })}
          className="rounded-control bg-brand px-4 py-2 text-ui text-brand-on-primary
                     transition-colors hover:brightness-110"
        >
          {this.props.retryLabel}
        </button>
      </div>
    );
  }
}
