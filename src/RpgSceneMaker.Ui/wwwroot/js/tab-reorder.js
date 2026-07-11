// Long-press drag-to-reorder for the bottom tab bar.
//
// Blazor owns the DOM, so — like the timeline editor — we attach ONE set of delegated pointer
// listeners to the <nav> bar and drive the gesture by mutating inline styles, only telling .NET the
// net result on drop (it rewrites the order, persists it, and re-renders the bar authoritatively).
//
// A short tap still navigates: we arm a timer on pointerdown and only enter "drag" mode once the
// finger has been held ~450ms without moving. Any movement before that cancels the arm (so the tap
// falls through to the normal <a> click). Tabs are equal-width grid columns, so the drop slot is just
// floor(pointerX / columnWidth). While dragging, the lifted tab follows the finger and the others
// slide aside to open a gap. On drop we clear every inline style (Blazor doesn't know about them, so a
// stale transform would otherwise stick) and swallow the click that a pointerup synthesizes.
window.rpgTabReorder = (() => {
    const bars = new WeakMap();

    const LONG_PRESS_MS = 450; // hold this long (roughly) to pick a tab up
    const CANCEL_PX = 10;      // moving further than this before the hold fires = it was a tap/scroll

    function init(bar, dotNetRef) {
        if (!bar || bars.has(bar)) return;

        const state = { dotNetRef, active: null, timer: null };

        const tabs = () => Array.from(bar.querySelectorAll("[data-tab-index]"));
        const columnWidth = () => {
            const n = tabs().length || 1;
            return bar.getBoundingClientRect().width / n;
        };

        const clearTimer = () => {
            if (state.timer !== null) { clearTimeout(state.timer); state.timer = null; }
        };

        // Reset the inline transforms/lift we applied so Blazor's re-render is authoritative.
        const clearStyles = () => {
            for (const el of tabs()) {
                el.style.transform = "";
                el.style.transition = "";
                el.style.zIndex = "";
                el.classList.remove("dragging");
            }
            bar.classList.remove("reordering");
        };

        // Slide the non-dragged tabs to open a gap where the lifted tab will drop.
        const layout = (a) => {
            const w = columnWidth();
            for (const el of tabs()) {
                const i = parseInt(el.dataset.tabIndex, 10);
                if (i === a.fromIndex) continue; // the lifted tab follows the finger instead
                let shift = 0;
                if (a.fromIndex < a.overIndex && i > a.fromIndex && i <= a.overIndex) shift = -w;
                else if (a.fromIndex > a.overIndex && i >= a.overIndex && i < a.fromIndex) shift = w;
                el.style.transform = shift ? `translateX(${shift}px)` : "";
            }
        };

        const onDown = (e) => {
            if (e.button !== undefined && e.button !== 0) return; // primary button / touch only
            if (state.active) return;
            const tabEl = e.target.closest("[data-tab-index]");
            if (!tabEl || !bar.contains(tabEl)) return;

            const index = parseInt(tabEl.dataset.tabIndex, 10);
            state.active = {
                tabEl,
                pointerId: e.pointerId,
                startX: e.clientX,
                startY: e.clientY,
                fromIndex: index,
                overIndex: index,
                dragging: false,
            };

            clearTimer();
            state.timer = setTimeout(() => {
                state.timer = null;
                const a = state.active;
                if (!a) return;
                a.dragging = true;
                try { bar.setPointerCapture(a.pointerId); } catch { }
                bar.classList.add("reordering");
                a.tabEl.classList.add("dragging");
                a.tabEl.style.zIndex = "2";
                if (navigator.vibrate) { try { navigator.vibrate(15); } catch { } }
            }, LONG_PRESS_MS);
        };

        const onMove = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;

            if (!a.dragging) {
                // Too much movement before the hold fired → treat as a tap/scroll, not a drag.
                if (Math.abs(e.clientX - a.startX) > CANCEL_PX || Math.abs(e.clientY - a.startY) > CANCEL_PX) {
                    clearTimer();
                    state.active = null;
                }
                return;
            }

            e.preventDefault();
            const rect = bar.getBoundingClientRect();
            const n = tabs().length;
            const over = Math.max(0, Math.min(n - 1, Math.floor((e.clientX - rect.left) / columnWidth())));
            a.tabEl.style.transform = `translateX(${e.clientX - a.startX}px)`;
            if (over !== a.overIndex) {
                a.overIndex = over;
                layout(a);
            }
        };

        const onUp = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;
            clearTimer();
            state.active = null;

            if (!a.dragging) return; // a plain tap — let the <a> click navigate

            try { bar.releasePointerCapture(a.pointerId); } catch { }
            clearStyles();
            suppressNextClick();
            if (a.overIndex !== a.fromIndex)
                state.dotNetRef.invokeMethodAsync("OnTabReordered", a.fromIndex, a.overIndex);
        };

        // A system gesture stealing the pointer mid-drag: drop it cleanly, no reorder.
        const onCancel = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;
            clearTimer();
            state.active = null;
            if (a.dragging) {
                try { bar.releasePointerCapture(a.pointerId); } catch { }
                clearStyles();
            }
        };

        // Swallow the click a drag's pointerup synthesizes so it doesn't navigate. Capture phase so we
        // beat Blazor's delegated (bubbling) click handler; self-removing, with a timeout safety net.
        const suppressNextClick = () => {
            const handler = (ev) => {
                ev.preventDefault();
                ev.stopPropagation();
                bar.removeEventListener("click", handler, true);
            };
            bar.addEventListener("click", handler, true);
            setTimeout(() => bar.removeEventListener("click", handler, true), 350);
        };

        bar.addEventListener("pointerdown", onDown);
        bar.addEventListener("pointermove", onMove);
        bar.addEventListener("pointerup", onUp);
        bar.addEventListener("pointercancel", onCancel);

        bars.set(bar, () => {
            clearTimer();
            bar.removeEventListener("pointerdown", onDown);
            bar.removeEventListener("pointermove", onMove);
            bar.removeEventListener("pointerup", onUp);
            bar.removeEventListener("pointercancel", onCancel);
        });
    }

    function dispose(bar) {
        const off = bars.get(bar);
        if (off) off();
        bars.delete(bar);
    }

    return { init, dispose };
})();
