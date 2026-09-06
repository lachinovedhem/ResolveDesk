import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./styles/index.css";

const container = document.getElementById("root");
if (!container) throw new Error("#root is missing from index.html.");

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

// Only in a built app: in dev the service worker would serve stale modules over HMR.
if (import.meta.env.PROD && "serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").catch((error) => {
      console.warn("Service worker registration failed:", error);
    });
  });
}
