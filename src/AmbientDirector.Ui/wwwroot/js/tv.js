// Keeps the player-facing /tv display awake with the Screen Wake Lock API — best effort only.
// Unsupported browsers (or a plain-HTTP LAN origin, where the API is unavailable) just do nothing;
// the TV then falls back to its own display settings. The lock is silently re-acquired when the tab
// becomes visible again (browsers release it on tab switch / screen off).
window.rpgTv = (() => {
    let sentinel = null;
    let wanted = false;

    async function acquire() {
        if (!wanted || !("wakeLock" in navigator)) return;
        try {
            sentinel = await navigator.wakeLock.request("screen");
        } catch (_) { /* denied/unsupported — degrade silently */ }
    }

    function onVisibilityChange() {
        if (document.visibilityState === "visible") acquire();
    }

    return {
        keepAwake() {
            wanted = true;
            document.addEventListener("visibilitychange", onVisibilityChange);
            acquire();
        },
        release() {
            wanted = false;
            document.removeEventListener("visibilitychange", onVisibilityChange);
            try { sentinel && sentinel.release(); } catch (_) { /* already gone */ }
            sentinel = null;
        },
    };
})();
