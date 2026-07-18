// Single-clip HTML5 audio preview for the sound-library browser. Only one preview plays at a time; a natural
// end (or a load/play error) notifies the owning Blazor component via a DotNet ref so it can reset its icon.
window.rpgSoundPreview = (function () {
    let audio = null;
    let owner = null; // DotNetObjectReference of the component that started the current clip

    function notifyEnded() {
        const o = owner;
        owner = null;
        if (o) {
            try { o.invokeMethodAsync('OnPreviewEnded'); } catch (e) { /* component already disposed */ }
        }
    }

    function stop() {
        // User-initiated stop: drop the owner first so pausing/clearing doesn't fire OnPreviewEnded back.
        owner = null;
        if (audio) {
            try { audio.pause(); } catch (e) { /* ignore */ }
            audio.removeEventListener('ended', notifyEnded);
            audio.removeEventListener('error', notifyEnded);
            audio.src = '';
            audio = null;
        }
    }

    function play(url, dotNetRef) {
        stop();
        audio = new Audio(url);
        owner = dotNetRef || null;
        audio.addEventListener('ended', notifyEnded);
        audio.addEventListener('error', notifyEnded);
        const p = audio.play();
        if (p && typeof p.catch === 'function') {
            p.catch(function () { notifyEnded(); });
        }
    }

    return { play: play, stop: stop };
})();
