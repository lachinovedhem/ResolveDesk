/*
 * Minimal offline shell. Deliberately conservative: the app shell is cached, API traffic never is —
 * a stale ticket queue would be worse than an honest "you are offline".
 */
const SHELL = "resolvedesk-shell-v1";
const SHELL_ASSETS = ["/", "/index.html", "/manifest.webmanifest", "/brand/logo.svg", "/brand/app-bg.svg"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(SHELL).then((cache) => cache.addAll(SHELL_ASSETS)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== SHELL).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;
  // Never serve stale data or a cached auth response.
  if (url.pathname.startsWith("/api") || url.pathname.startsWith("/health")) return;

  // Navigations: network first, shell as the offline fallback.
  if (request.mode === "navigate") {
    event.respondWith(fetch(request).catch(() => caches.match("/index.html")));
    return;
  }

  // Static assets: cache first, then fill the cache in the background.
  event.respondWith(
    caches.match(request).then((hit) =>
      hit ??
      fetch(request).then((response) => {
        if (response.ok) {
          const copy = response.clone();
          caches.open(SHELL).then((cache) => cache.put(request, copy));
        }
        return response;
      }),
    ),
  );
});
