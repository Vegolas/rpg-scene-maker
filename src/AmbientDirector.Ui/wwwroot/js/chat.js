// Scrolls the assistant chat's own log container to the bottom. Used by the in-editor
// drawer, whose transcript scrolls inside its panel rather than scrolling the window
// (the full-page Assistant keeps using window.scrollTo). Best-effort: a null element
// (e.g. a poll that fires before the log has rendered) is simply ignored.
window.rpgChat = {
    scrollToBottom(el) {
        if (el) el.scrollTop = el.scrollHeight;
    },
};
