// Minimal service worker — its job is to make the panel installable as a PWA,
// not to run it offline. Scene Maker is a LAN control surface that needs a live
// API (lights, Spotify, soundboard) and a stored API key, so we deliberately do
// NOT cache application, framework or API responses: a stale shell could serve
// outdated fingerprinted JS after a deploy or shadow the key in localStorage.
//
// We only pre-cache a tiny offline fallback shell and always hit the network
// first for navigations. skipWaiting + clients.claim make a new server build's
// worker take over immediately (server and panel always ship together).
const CACHE = 'rpg-scene-maker-shell-v1';
const SHELL = ['./', './index.html', './manifest.webmanifest', './icon-192.png'];

self.addEventListener('install', (event) => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(CACHE).then((cache) => cache.addAll(SHELL)).catch(() => {})
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)));
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  // Only handle top-level navigations; everything else (API calls, fingerprinted
  // framework assets, images) goes straight to the network, never a stale cache.
  if (req.method === 'GET' && req.mode === 'navigate') {
    event.respondWith(fetch(req).catch(() => caches.match('./index.html')));
  }
});
