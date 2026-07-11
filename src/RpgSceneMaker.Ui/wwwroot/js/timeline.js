// Pointer-based drag / resize for the event timeline editor.
//
// Blazor owns the DOM, so we can't let it fight us mid-gesture. Instead we attach ONE set of
// delegated pointer listeners to the timeline root and, on pointerdown over a clip (or its resize
// handle), capture the pointer to the root (`setPointerCapture` keeps the drag alive even if the
// finger leaves the element) and move/resize the clip element *visually* by mutating inline style.
// On pointerup we report the net delta in milliseconds back to .NET, which updates its model and
// re-renders — snapping the clip to its authoritative position. A negligible move is reported as a
// tap (deltaMs 0) so .NET just selects the clip.
//
// Touch: clips carry `touch-action: none` so a drag never scrolls the track; the empty track area
// keeps default touch-action so panning the timeline still works (pointerdown there matches no clip
// and we bail before preventing default).
window.rpgTimeline = (() => {
    const roots = new WeakMap();

    function init(root, dotNetRef) {
        if (!root || roots.has(root)) return;

        const state = { dotNetRef, active: null };

        const onDown = (e) => {
            if (e.button !== undefined && e.button !== 0) return; // primary button / touch only
            if (state.active) return; // a gesture is already in flight — ignore a second finger
            const clipEl = e.target.closest(".tl-clip");
            if (!clipEl || !root.contains(clipEl)) return;

            const isResize = !!e.target.closest(".tl-resize");
            const pps = parseFloat(root.dataset.pps) || 60;

            state.active = {
                clipEl,
                mode: isResize ? "resize" : "move",
                kind: clipEl.dataset.clipKind,
                id: clipEl.dataset.clipId,
                startX: e.clientX,
                origLeft: parseFloat(clipEl.style.left) || 0,
                origWidth: clipEl.getBoundingClientRect().width,
                pps,
                pointerId: e.pointerId,
            };
            clipEl.classList.add("tl-dragging");
            try { root.setPointerCapture(e.pointerId); } catch { }
            e.preventDefault();
            e.stopPropagation();
        };

        const onMove = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;
            const dx = e.clientX - a.startX;
            if (a.mode === "move") {
                a.clipEl.style.left = Math.max(0, a.origLeft + dx) + "px";
            } else {
                a.clipEl.style.width = Math.max(10, a.origWidth + dx) + "px";
            }
            e.preventDefault();
        };

        // Restore the pre-drag inline geometry so Blazor's re-render is authoritative even when the
        // gesture snaps back to the same position (Blazor skips a DOM write when its value is unchanged,
        // which would otherwise leave our mid-drag override on screen).
        const restoreGeometry = (a) => {
            if (a.mode === "move") a.clipEl.style.left = a.origLeft + "px";
            else a.clipEl.style.width = a.origWidth + "px";
        };

        const onUp = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;
            state.active = null;
            a.clipEl.classList.remove("tl-dragging");
            try { root.releasePointerCapture(a.pointerId); } catch { }

            restoreGeometry(a);

            const dx = e.clientX - a.startX;
            const deltaMs = Math.round((dx / a.pps) * 1000);
            // < ~4px counts as a tap: report deltaMs 0 so .NET only selects the clip.
            const netMs = Math.abs(dx) < 4 ? 0 : deltaMs;
            state.dotNetRef.invokeMethodAsync("OnClipDragEnd", a.kind, a.id, a.mode, netMs);
        };

        // pointercancel (e.g. an iPad system gesture stealing the pointer mid-drag) ABORTS the gesture:
        // restore the pre-drag geometry and drop it without computing a delta or notifying .NET, so a
        // stale/zero cancel clientX can't teleport the clip.
        const onCancel = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;
            state.active = null;
            a.clipEl.classList.remove("tl-dragging");
            try { root.releasePointerCapture(a.pointerId); } catch { }
            restoreGeometry(a);
        };

        root.addEventListener("pointerdown", onDown);
        root.addEventListener("pointermove", onMove);
        root.addEventListener("pointerup", onUp);
        root.addEventListener("pointercancel", onCancel);

        roots.set(root, () => {
            root.removeEventListener("pointerdown", onDown);
            root.removeEventListener("pointermove", onMove);
            root.removeEventListener("pointerup", onUp);
            root.removeEventListener("pointercancel", onCancel);
        });
    }

    function dispose(root) {
        const off = roots.get(root);
        if (off) off();
        roots.delete(root);
    }

    // Inner width available for the scrolling track viewport (used by the "fit" zoom).
    function viewportWidth(root) {
        const vp = root?.querySelector(".tl-scroll");
        return vp ? vp.clientWidth : 0;
    }

    return { init, dispose, viewportWidth };
})();
