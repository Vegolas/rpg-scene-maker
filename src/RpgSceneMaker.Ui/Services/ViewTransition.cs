using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace RpgSceneMaker.Ui.Services;

/// <summary>
/// Runs page navigations and same-page state changes behind the browser
/// <see href="https://developer.mozilla.org/docs/Web/API/View_Transitions_API">View Transitions API</see>.
/// </summary>
/// <remarks>
/// The transition is started in JavaScript (<c>rpgViewTransitions.run</c>); its update
/// callback invokes <see cref="ApplyAsync"/> here, which runs the queued mutation and
/// then yields so Blazor flushes its render before the browser captures the new frame.
/// So the DOM change happens <em>inside</em> the transition — the only way to make the
/// API cooperate with a framework that owns its own rendering. Falls back to an
/// un-animated mutation when JS interop or the API itself is unavailable.
/// </remarks>
public sealed class ViewTransition(IJSRuntime js, NavigationManager nav) : IDisposable
{
    private DotNetObjectReference<ViewTransition>? _ref;
    private Func<Task>? _pending;

    /// <summary>Navigates to <paramref name="uri"/> behind a "nav" view transition.</summary>
    public Task NavigateAsync(string uri) =>
        RunAsync(() => nav.NavigateTo(uri), type: "nav");

    /// <summary>Applies a same-page state change behind a view transition.</summary>
    /// <param name="mutate">The state change (e.g. mark a scene active) plus its StateHasChanged.</param>
    /// <param name="type">CSS transition tag, surfaced as <c>:root[data-view-transition]</c>.</param>
    public Task RunAsync(Action mutate, string? type = null) =>
        RunAsync(() => { mutate(); return Task.CompletedTask; }, type);

    /// <inheritdoc cref="RunAsync(Action, string?)"/>
    public async Task RunAsync(Func<Task> mutate, string? type = null)
    {
        _pending = mutate;
        try
        {
            await EnsureInitializedAsync();
            await js.InvokeVoidAsync("rpgViewTransitions.run", type);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException or TaskCanceledException)
        {
            // Interop unavailable (script missing, or shutting down) — apply the change anyway.
            if (_pending is { } pending)
            {
                _pending = null;
                await pending();
            }
        }
    }

    /// <summary>Invoked from the transition's update callback; performs the queued mutation.</summary>
    [JSInvokable]
    public async Task ApplyAsync()
    {
        if (_pending is not { } mutate) return;
        _pending = null;

        await mutate();
        // Let Blazor's render queue drain so the API captures the post-mutation DOM.
        await Task.Yield();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_ref is not null) return;
        _ref = DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("rpgViewTransitions.init", _ref);
    }

    public void Dispose() => _ref?.Dispose();
}
