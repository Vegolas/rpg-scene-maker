// Browser-side "pick a file, crop + downscale it" pipeline for tile art.
//
// The whole thing lives in JS so only the small cropped data URL crosses the
// .NET interop boundary (never the full-resolution original). Blazor calls
// rpgImageCrop.cropFromFilePicker(aspectW, aspectH, maxDim) and awaits a
// data URL (webp when the browser can encode it, else jpeg) or null if the
// user cancels. Touch-first: pan/pinch/zoom share one Pointer Events path so
// the iPad and a desktop mouse behave identically.
window.rpgImageCrop = (() => {
    const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
    const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);

    // Open the file picker, then (if a file is chosen) the crop editor.
    function cropFromFilePicker(aspectW, aspectH, maxDim) {
        aspectW = aspectW > 0 ? aspectW : 4;
        aspectH = aspectH > 0 ? aspectH : 3;
        maxDim = maxDim > 0 ? maxDim : 640;

        return new Promise((resolve) => {
            let settled = false;
            const done = (value) => { if (!settled) { settled = true; resolve(value); } };

            const input = document.createElement("input");
            input.type = "file";
            input.accept = "image/*";
            input.style.position = "fixed";
            input.style.left = "-9999px";
            input.style.opacity = "0";

            input.addEventListener("change", () => {
                const file = input.files && input.files[0];
                input.remove();
                if (!file) { done(null); return; }
                readFile(file, (img) => {
                    if (img) openEditor(img, aspectW, aspectH, maxDim, done);
                    else done(null);
                });
            });
            // Modern browsers fire 'cancel' when the picker is dismissed with no choice.
            input.addEventListener("cancel", () => { input.remove(); done(null); });

            document.body.appendChild(input);
            input.click();
        });
    }

    function readFile(file, cb) {
        const reader = new FileReader();
        reader.onload = () => {
            const img = new Image();
            img.onload = () => cb(img);
            img.onerror = () => cb(null);
            img.src = reader.result;
        };
        reader.onerror = () => cb(null);
        reader.readAsDataURL(file);
    }

    function openEditor(img, aspectW, aspectH, maxDim, done) {
        const iw = img.naturalWidth || img.width;
        const ih = img.naturalHeight || img.height;
        if (!iw || !ih) { done(null); return; }

        // ----- build the overlay -----
        const overlay = el("div", "crop-overlay");
        const stage = el("div", "crop-stage");
        const canvas = el("canvas", "crop-canvas");
        canvas.style.touchAction = "none";
        stage.appendChild(canvas);

        const controls = el("div", "crop-controls");
        const cancelBtn = el("button", "crop-btn");
        cancelBtn.type = "button";
        cancelBtn.textContent = "Cancel";
        const zoom = el("input", "crop-zoom");
        zoom.type = "range";
        zoom.min = "1";
        zoom.max = "4";
        zoom.step = "0.01";
        zoom.value = "1";
        zoom.setAttribute("aria-label", "Zoom");
        const useBtn = el("button", "crop-btn crop-btn-primary");
        useBtn.type = "button";
        useBtn.textContent = "Use";
        controls.append(cancelBtn, zoom, useBtn);

        overlay.append(stage, controls);
        document.body.appendChild(overlay);

        const ctx = canvas.getContext("2d");

        // ----- view state (all in CSS pixels of the on-screen canvas) -----
        let canvasW = 0, canvasH = 0, dpr = 1;
        let coverScale = 1;   // scale that makes the image exactly cover the frame
        let zoomVal = 1;      // 1..4, on top of coverScale
        let offsetX = 0, offsetY = 0; // image top-left within the canvas
        let centered = false;

        const drawnW = () => iw * coverScale * zoomVal;
        const drawnH = () => ih * coverScale * zoomVal;

        function clampOffsets() {
            offsetX = clamp(offsetX, canvasW - drawnW(), 0);
            offsetY = clamp(offsetY, canvasH - drawnH(), 0);
        }

        function draw() {
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.clearRect(0, 0, canvasW, canvasH);
            ctx.drawImage(img, offsetX, offsetY, drawnW(), drawnH());
        }

        // Fit the fixed aspectW:aspectH frame inside the stage, size the canvas,
        // recompute the cover scale, and (first time) centre the image.
        function layout() {
            const availW = stage.clientWidth;
            const availH = stage.clientHeight;
            if (availW < 4 || availH < 4) return;

            let w = availW;
            let h = (w * aspectH) / aspectW;
            if (h > availH) { h = availH; w = (h * aspectW) / aspectH; }

            dpr = window.devicePixelRatio || 1;
            canvasW = Math.round(w);
            canvasH = Math.round(h);
            canvas.style.width = canvasW + "px";
            canvas.style.height = canvasH + "px";
            canvas.width = Math.round(canvasW * dpr);
            canvas.height = Math.round(canvasH * dpr);

            coverScale = Math.max(canvasW / iw, canvasH / ih);
            if (!centered) {
                offsetX = (canvasW - drawnW()) / 2;
                offsetY = (canvasH - drawnH()) / 2;
                centered = true;
            }
            clampOffsets();
            draw();
        }

        // Zoom keeping the image point under (cx,cy) fixed; centre when not given.
        function setZoom(next, cx, cy) {
            next = clamp(next, 1, 4);
            if (cx == null) { cx = canvasW / 2; cy = canvasH / 2; }
            const s0 = coverScale * zoomVal;
            const imgX = (cx - offsetX) / s0;
            const imgY = (cy - offsetY) / s0;
            zoomVal = next;
            const s1 = coverScale * zoomVal;
            offsetX = cx - imgX * s1;
            offsetY = cy - imgY * s1;
            clampOffsets();
            draw();
            if (zoom.value !== String(zoomVal)) zoom.value = String(zoomVal);
        }

        // ----- pointer pan / pinch -----
        const pointers = new Map();
        let pinchDist = 0, pinchZoom = 1;

        const localMid = (pts) => {
            const rect = canvas.getBoundingClientRect();
            return { x: (pts[0].x + pts[1].x) / 2 - rect.left, y: (pts[0].y + pts[1].y) / 2 - rect.top };
        };

        function onPointerDown(e) {
            pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            try { canvas.setPointerCapture(e.pointerId); } catch (_) { /* ignore */ }
            if (pointers.size === 2) {
                const pts = [...pointers.values()];
                pinchDist = dist(pts[0], pts[1]);
                pinchZoom = zoomVal;
            }
        }

        function onPointerMove(e) {
            if (!pointers.has(e.pointerId)) return;
            const prev = pointers.get(e.pointerId);
            const cur = { x: e.clientX, y: e.clientY };
            pointers.set(e.pointerId, cur);

            if (pointers.size >= 2) {
                const pts = [...pointers.values()];
                const d = dist(pts[0], pts[1]);
                if (pinchDist > 0) {
                    const mid = localMid(pts);
                    setZoom(pinchZoom * (d / pinchDist), mid.x, mid.y);
                }
            } else {
                offsetX += cur.x - prev.x;
                offsetY += cur.y - prev.y;
                clampOffsets();
                draw();
            }
        }

        function onPointerUp(e) {
            pointers.delete(e.pointerId);
            try { canvas.releasePointerCapture(e.pointerId); } catch (_) { /* ignore */ }
            if (pointers.size < 2) pinchDist = 0;
        }

        function onWheel(e) {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            setZoom(zoomVal * Math.exp(-e.deltaY * 0.0015), e.clientX - rect.left, e.clientY - rect.top);
        }

        const onZoomInput = () => setZoom(parseFloat(zoom.value));
        const onKeyDown = (e) => { if (e.key === "Escape") close(null); };
        const onBackdrop = (e) => { if (e.target === overlay) close(null); };
        const onResize = () => layout();

        function use() {
            const s = coverScale * zoomVal;
            const srcX = clamp(-offsetX / s, 0, iw);
            const srcY = clamp(-offsetY / s, 0, ih);
            const srcW = Math.min(canvasW / s, iw - srcX);
            const srcH = Math.min(canvasH / s, ih - srcY);

            let outW, outH;
            if (aspectW >= aspectH) { outW = maxDim; outH = Math.round((maxDim * aspectH) / aspectW); }
            else { outH = maxDim; outW = Math.round((maxDim * aspectW) / aspectH); }

            const out = document.createElement("canvas");
            out.width = outW;
            out.height = outH;
            const octx = out.getContext("2d");
            octx.imageSmoothingEnabled = true;
            octx.imageSmoothingQuality = "high";
            octx.drawImage(img, srcX, srcY, srcW, srcH, 0, 0, outW, outH);

            let url = out.toDataURL("image/webp", 0.82);
            if (url.indexOf("data:image/webp") !== 0) url = out.toDataURL("image/jpeg", 0.85);
            close(url);
        }

        function close(result) {
            window.removeEventListener("keydown", onKeyDown);
            window.removeEventListener("resize", onResize);
            canvas.removeEventListener("pointerdown", onPointerDown);
            canvas.removeEventListener("pointermove", onPointerMove);
            canvas.removeEventListener("pointerup", onPointerUp);
            canvas.removeEventListener("pointercancel", onPointerUp);
            canvas.removeEventListener("wheel", onWheel);
            zoom.removeEventListener("input", onZoomInput);
            overlay.removeEventListener("pointerdown", onBackdrop);
            cancelBtn.removeEventListener("click", onCancel);
            useBtn.removeEventListener("click", use);
            overlay.remove();
            done(result);
        }

        const onCancel = () => close(null);

        canvas.addEventListener("pointerdown", onPointerDown);
        canvas.addEventListener("pointermove", onPointerMove);
        canvas.addEventListener("pointerup", onPointerUp);
        canvas.addEventListener("pointercancel", onPointerUp);
        canvas.addEventListener("wheel", onWheel, { passive: false });
        zoom.addEventListener("input", onZoomInput);
        overlay.addEventListener("pointerdown", onBackdrop);
        cancelBtn.addEventListener("click", onCancel);
        useBtn.addEventListener("click", use);
        window.addEventListener("keydown", onKeyDown);
        window.addEventListener("resize", onResize);

        // Let the overlay lay out before measuring the stage.
        requestAnimationFrame(layout);
    }

    function el(tag, className) {
        const node = document.createElement(tag);
        node.className = className;
        return node;
    }

    return { cropFromFilePicker };
})();
