// Register the PWA service worker (see /service-worker.js). Best-effort: a
// failure here must never keep the panel from loading, so errors are swallowed.
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('service-worker.js').catch(() => {});
  });
}
