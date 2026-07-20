// Pointer-based drag / resize for the board editor's live 16:9 preview.
//
// Blazor owns the DOM and re-renders the preview on every keystroke (the page rebuilds its render model each
// render), so we can't let it fight us mid-gesture. Instead we attach ONE set of delegated pointer listeners
// to the editor OVERLAY (the invisible layer of hit boxes BoardCanvasEditor draws over the canvas) and, on
// pointerdown over a box (or one of its resize handles), capture the pointer to the overlay and move/resize the
// box *visually* by mutating its inline style. We mutate the canvas "twin" (the matching `.board-el` inside the
// read-only BoardCanvas) in lockstep, so the real element the user sees actually moves — the overlay box itself
// is transparent. On pointerup we report the final geometry (percent of the stage) back to .NET, which clamps
// it, updates its model and re-renders — snapping everything to the authoritative position. A negligible move
// is a tap (no geometry report); the selection itself already happened on pointerdown.
//
// Geometry is percent-of-stage throughout: X/Y 0–100 (top-left anchor), W/H 0.1–100. Pixel deltas are converted
// with the overlay's bounding rect, cached once at gesture start.
//
// Touch: boxes carry `touch-action: none` (static CSS) so a drag never scrolls the page; the empty overlay area
// keeps the default touch-action so panning the page from blank stage still works (a pointerdown there matches
// no box and we bail before capturing or preventing default).
window.rpgBoardEditor = (() => {
    const roots = new WeakMap();

    const clamp = (v, lo, hi) => Math.min(Math.max(v, lo), hi);
    // Trim float noise to match BoardCanvas's "0.###" formatting so our live values read like the model's.
    const fmt = (v) => (Math.round(v * 1000) / 1000);

    function init(root, dotNetRef) {
        if (!root || roots.has(root)) return;

        // active: the in-flight gesture (null between gestures). lastTap: {index, time} for double-tap detection.
        const state = { dotNetRef, active: null, lastTap: null };

        // The canvas twin for element `index`: BoardCanvas renders an optional `<img class="board-canvas__bg">`
        // (NOT a `.board-el`) followed by one `.board-el` per element in order, so `:scope > .board-el` is
        // index-aligned with Model.Elements — the same order as our overlay boxes.
        const twinFor = (index) => {
            const canvas = root.parentElement?.querySelector(".board-canvas");
            if (!canvas) return null;
            return canvas.querySelectorAll(":scope > .board-el")[index] || null;
        };

        // Write a geometry to both the overlay box and its twin so the visible element tracks the gesture.
        const writeGeom = (a, x, y, w, h) => {
            const px = fmt(x) + "%", py = fmt(y) + "%", pw = fmt(w) + "%", ph = fmt(h) + "%";
            a.box.style.left = px; a.box.style.top = py; a.box.style.width = pw; a.box.style.height = ph;
            if (a.twin) {
                a.twin.style.left = px; a.twin.style.top = py; a.twin.style.width = pw; a.twin.style.height = ph;
            }
            a.last = { x, y, w, h };
        };

        // Restore the pre-gesture inline geometry on both nodes BEFORE reporting to .NET. Blazor skips a DOM
        // write when its rendered value is unchanged, so if the gesture lands back at (or near) the start our
        // mid-drag override would otherwise stay on screen. Restoring lets Blazor's re-render be authoritative.
        const restore = (a) => {
            const { origX, origY, origW, origH } = a;
            writeGeom(a, origX, origY, origW, origH);
        };

        const onDown = (e) => {
            if (e.button !== undefined && e.button !== 0) return; // primary button / touch only
            if (state.active) return;                             // a gesture is already in flight — ignore

            const handle = e.target.closest(".bce-h");
            const box = e.target.closest(".bce-box");
            // Empty overlay area (no box/handle): deselect and BAIL without capture/preventDefault, so touch
            // panning the page from blank stage still works.
            if (!box || !root.contains(box)) {
                state.dotNetRef.invokeMethodAsync("OnCanvasSelect", -1);
                return;
            }

            const index = parseInt(box.dataset.index, 10);
            const rect = root.getBoundingClientRect(); // px→% basis, cached for the whole gesture

            state.active = {
                box,
                twin: twinFor(index),
                index,
                mode: handle ? "resize" : "move",
                dir: handle ? handle.dataset.h : null,
                pointerId: e.pointerId,
                startX: e.clientX,
                startY: e.clientY,
                rect,
                // Original geometry parsed from the box's inline "12.5%" strings.
                origX: parseFloat(box.style.left) || 0,
                origY: parseFloat(box.style.top) || 0,
                origW: parseFloat(box.style.width) || 0,
                origH: parseFloat(box.style.height) || 0,
                last: null,
            };

            // We preventDefault below, which suppresses the browser's native focus-on-mousedown; focus the box
            // explicitly so keyboard nudging works right after a tap.
            box.focus();

            // Select on pointerdown if this box wasn't already selected. A mid-gesture re-render is safe: Blazor
            // patches the class attribute in place and inserts the handle children, but the box NODE survives
            // (positional diff, keyed by index), and because the model geometry hasn't changed Blazor skips
            // rewriting the style attribute — so the live style mutations we make below survive the re-render.
            if (!box.classList.contains("is-selected")) {
                state.dotNetRef.invokeMethodAsync("OnCanvasSelect", index);
            }

            state.active.last = { x: state.active.origX, y: state.active.origY, w: state.active.origW, h: state.active.origH };
            try { root.setPointerCapture(e.pointerId); } catch { }
            box.classList.add("is-dragging");
            e.preventDefault();
        };

        const onMove = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;
            const dxPct = (e.clientX - a.startX) / a.rect.width * 100;
            const dyPct = (e.clientY - a.startY) / a.rect.height * 100;

            if (a.mode === "move") {
                writeGeom(a, clamp(a.origX + dxPct, 0, 100), clamp(a.origY + dyPct, 0, 100), a.origW, a.origH);
            } else {
                // Resize: the opposite edge/corner stays fixed and the box never inverts. Decompose the handle
                // direction into its horizontal (e/w) and vertical (n/s) parts.
                let nx = a.origX, ny = a.origY, nw = a.origW, nh = a.origH;
                if (a.dir.includes("e")) {
                    nw = clamp(a.origW + dxPct, 0.1, 100);
                }
                if (a.dir.includes("w")) {
                    const right = a.origX + a.origW;              // fixed right edge
                    nx = clamp(a.origX + dxPct, 0, right - 0.1);
                    nw = clamp(right - nx, 0.1, 100);
                }
                if (a.dir.includes("s")) {
                    nh = clamp(a.origH + dyPct, 0.1, 100);
                }
                if (a.dir.includes("n")) {
                    const bottom = a.origY + a.origH;             // fixed bottom edge
                    ny = clamp(a.origY + dyPct, 0, bottom - 0.1);
                    nh = clamp(bottom - ny, 0.1, 100);
                }
                // X/Y also clamp into [0,100] (an element MAY still hang off the right/bottom via its width).
                writeGeom(a, clamp(nx, 0, 100), clamp(ny, 0, 100), nw, nh);
            }
            e.preventDefault();
        };

        const onUp = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;
            state.active = null;
            a.box.classList.remove("is-dragging");
            try { root.releasePointerCapture(a.pointerId); } catch { }

            const travel = Math.hypot(e.clientX - a.startX, e.clientY - a.startY);
            const last = a.last;
            restore(a); // hand authority back to Blazor before reporting (see restore()).

            if (travel < 4) {
                // A tap: selection already fired on pointerdown, so report no geometry. A second tap on the same
                // box within 350ms is a double-tap. We roll our own (mouse + touch) rather than use dblclick,
                // which iOS Safari does not reliably fire on touch.
                const now = performance.now();
                if (state.lastTap && state.lastTap.index === a.index && (now - state.lastTap.time) < 350) {
                    state.dotNetRef.invokeMethodAsync("OnCanvasTextTap", a.index);
                    state.lastTap = null; // consumed, so a third tap doesn't immediately re-fire
                } else {
                    state.lastTap = { index: a.index, time: now };
                }
            } else {
                state.lastTap = null; // a drag is not a tap
                state.dotNetRef.invokeMethodAsync("OnCanvasGeometry", a.index, fmt(last.x), fmt(last.y), fmt(last.w), fmt(last.h));
            }
        };

        // pointercancel (e.g. an iPad system gesture stealing the pointer mid-drag) ABORTS: restore the
        // pre-drag geometry and drop the gesture without reporting, so a stale/zero cancel position can't
        // teleport the element.
        const onCancel = (e) => {
            const a = state.active;
            if (!a || e.pointerId !== a.pointerId) return;
            state.active = null;
            a.box.classList.remove("is-dragging");
            try { root.releasePointerCapture(a.pointerId); } catch { }
            restore(a);
        };

        // Keyboard nudge / deselect on the focused box (event bubbles up to the overlay root).
        const onKey = (e) => {
            const box = e.target.closest && e.target.closest(".bce-box");
            if (!box || !root.contains(box)) return;
            const index = parseInt(box.dataset.index, 10);

            let dx = 0, dy = 0;
            switch (e.key) {
                case "ArrowLeft": dx = -1; break;
                case "ArrowRight": dx = 1; break;
                case "ArrowUp": dy = -1; break;
                case "ArrowDown": dy = 1; break;
                case "Escape":
                    state.dotNetRef.invokeMethodAsync("OnCanvasSelect", -1);
                    box.blur();
                    return;
                default:
                    return; // leave every other key (Tab!) alone so focus navigation keeps working
            }
            e.preventDefault(); // arrows would scroll the page otherwise
            state.dotNetRef.invokeMethodAsync("OnCanvasNudge", index, dx, dy, e.shiftKey);
        };

        root.addEventListener("pointerdown", onDown);
        root.addEventListener("pointermove", onMove);
        root.addEventListener("pointerup", onUp);
        root.addEventListener("pointercancel", onCancel);
        root.addEventListener("keydown", onKey);

        roots.set(root, () => {
            root.removeEventListener("pointerdown", onDown);
            root.removeEventListener("pointermove", onMove);
            root.removeEventListener("pointerup", onUp);
            root.removeEventListener("pointercancel", onCancel);
            root.removeEventListener("keydown", onKey);
        });
    }

    function dispose(root) {
        const off = roots.get(root);
        if (off) off();
        roots.delete(root);
    }

    // ---- small helpers the page calls with a plain element index (no gesture state) ----

    // Scroll the matching element row into view (canvas-originated selection reveals it in the list).
    function revealRow(index) {
        document.querySelector('.board-elrow[data-index="' + index + '"]')
            ?.scrollIntoView({ block: "nearest", behavior: "smooth" });
    }

    // Reveal the row AND focus its text area (double-tapping a text element jumps straight to editing it).
    function focusRowText(index) {
        const row = document.querySelector('.board-elrow[data-index="' + index + '"]');
        if (!row) return;
        row.scrollIntoView({ block: "nearest", behavior: "smooth" });
        row.querySelector("textarea")?.focus();
    }

    return { init, dispose, revealRow, focusRowText };
})();
