// Bridges the browser View Transitions API to Blazor's renderer.
//
// A view transition must: capture the OLD DOM, run the DOM mutation, then capture
// the NEW DOM. Blazor owns rendering, so we can't hand the API a callback that
// mutates the DOM directly. Instead we start the transition here and, from inside
// its update callback, invoke back into .NET (`ApplyAsync`). That method performs
// the navigation / state change and only resolves once Blazor has re-rendered — so
// the OLD DOM is captured before it runs and the NEW DOM right after.
//
// Degrades gracefully: when the API is missing or the user prefers reduced motion,
// the mutation is applied immediately with no animation.
window.rpgViewTransitions = (() => {
    let dotNet = null;

    const prefersReducedMotion = () =>
        window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;

    const canAnimate = () =>
        typeof document.startViewTransition === "function" && !prefersReducedMotion();

    return {
        // Called once from ViewTransition.EnsureInitializedAsync with a DotNetObjectReference.
        init(ref) {
            dotNet = ref;
        },

        // `type` tags the transition (e.g. "nav", "scene") so CSS can style it via
        // :root[data-view-transition="..."]. It's cleared when the transition ends.
        run(type) {
            if (dotNet === null) return;

            if (!canAnimate()) {
                dotNet.invokeMethodAsync("ApplyAsync");
                return;
            }

            const root = document.documentElement;
            if (type) root.dataset.viewTransition = type;

            const transition = document.startViewTransition(
                () => dotNet.invokeMethodAsync("ApplyAsync"));

            if (type) {
                transition.finished.finally(() => {
                    if (root.dataset.viewTransition === type) {
                        delete root.dataset.viewTransition;
                    }
                });
            }
        },
    };
})();
