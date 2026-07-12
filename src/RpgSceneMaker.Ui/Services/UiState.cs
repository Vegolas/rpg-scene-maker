namespace RpgSceneMaker.Ui.Services;

/// <summary>Shared UI state: toast messages and API connectivity, rendered by the layout.</summary>
public class UiState
{
    private int _toastVersion;

    public string? Toast { get; private set; }
    public bool ToastIsError { get; private set; }
    public bool Connected { get; private set; } = true;

    /// <summary>Per-device developer mode (persisted in localStorage by <see cref="ApiClient"/>).
    /// When on, the panel surfaces the Logs tab and its diagnostics panel.</summary>
    public bool DevMode { get; private set; }

    /// <summary>Whether an AI provider key is saved server-side — gates the Assistant nav tab.</summary>
    public bool AssistantConfigured { get; private set; }

    /// <summary>The current header search text. Searchable pages filter their own list by this and
    /// subscribe to <see cref="SearchChanged"/> to re-render as it changes; the layout owns the input
    /// and decides (from the route) when the search bar is shown. Reset to "" on every navigation.</summary>
    public string SearchQuery { get; private set; } = "";

    /// <summary>Preferred order of the bottom-bar tabs, as their hrefs. Empty until loaded / never
    /// customized. The layout renders tabs in this order (unknown/new tabs fall to the end);
    /// long-press-drag on the bar rewrites it and <see cref="ApiClient"/> persists it per-device.</summary>
    public IReadOnlyList<string> TabOrder { get; private set; } = [];

    public event Action? Changed;

    /// <summary>Raised only when <see cref="SearchQuery"/> changes, so pages re-filter without also
    /// re-rendering on every toast/connection tick that fires <see cref="Changed"/>.</summary>
    public event Action? SearchChanged;

    public void ReportOk(string message) => ShowToast(message, isError: false);

    public void ReportError(string message) => ShowToast(message, isError: true);

    public void SetConnected(bool connected)
    {
        if (Connected == connected) return;
        Connected = connected;
        Changed?.Invoke();
    }

    public void SetDevMode(bool on)
    {
        if (DevMode == on) return;
        DevMode = on;
        Changed?.Invoke();
    }

    public void SetAssistantConfigured(bool configured)
    {
        if (AssistantConfigured == configured) return;
        AssistantConfigured = configured;
        Changed?.Invoke();
    }

    public void SetSearchQuery(string? query)
    {
        query ??= "";
        if (query == SearchQuery) return;
        SearchQuery = query;
        SearchChanged?.Invoke();
    }

    public void SetTabOrder(IReadOnlyList<string> order)
    {
        TabOrder = order ?? [];
        Changed?.Invoke();
    }

    private async void ShowToast(string message, bool isError)
    {
        var version = ++_toastVersion;
        Toast = message;
        ToastIsError = isError;
        Changed?.Invoke();

        await Task.Delay(isError ? 5000 : 2500);
        if (version != _toastVersion) return; // a newer toast replaced this one
        Toast = null;
        Changed?.Invoke();
    }
}
