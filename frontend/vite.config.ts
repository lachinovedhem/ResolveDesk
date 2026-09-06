import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { "@": path.resolve(__dirname, "src") } },
  server: {
    port: 5173,
    // The API's CORS policy allows this origin by default; the proxy keeps the browser same-origin
    // anyway, so cookies and dev tooling behave the same as in production behind one host.
    proxy: {
      "/api": { target: process.env.VITE_API_URL ?? "http://localhost:8080", changeOrigin: true },
      "/health": { target: process.env.VITE_API_URL ?? "http://localhost:8080", changeOrigin: true },
    },
  },
  build: {
    // AG Grid is only reached through a lazy import on desktop, so it gets its own chunk and never
    // lands in the entry bundle. 1 MB is expected for it — the warning would only be noise.
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: {
        manualChunks: {
          grid: ["ag-grid-community", "ag-grid-react"],
          react: ["react", "react-dom", "react-router-dom"],
        },
      },
    },
  },
});
