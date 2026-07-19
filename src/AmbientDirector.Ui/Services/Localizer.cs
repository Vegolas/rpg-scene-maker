using System.Globalization;
using Microsoft.JSInterop;
using AmbientDirector.Ui.Contracts;

namespace AmbientDirector.Ui.Services;

/// <summary>
/// The panel's runtime translator. Holds the active language's string table plus the English table as a
/// per-key fallback, so a translation missing a key shows English rather than a blank or a raw key. English
/// itself is served by the API (with an embedded copy as the ultimate fallback), so the panel never carries
/// its own duplicate of the strings.
///
/// Switching language is instant — no page reload: swap the active table and raise <see cref="Changed"/>,
/// which the layout turns into a root re-render (the same event→re-render idiom as <see cref="UiState"/>).
///
/// Look up text by dotted key: <c>L["nav.scenes"]</c>. Parameterized text uses <see cref="Format"/> with
/// <c>{0}</c> placeholders; count-dependent text uses <see cref="Plural"/> (English one/other, Polish
/// one/few/many). Note: this deliberately does not mutate <see cref="CultureInfo.CurrentCulture"/> — the UI
/// has no locale-sensitive number/date display and the timeline parses with the invariant culture, so
/// switching the thread culture would only risk parse regressions.
/// </summary>
public class Localizer(ApiClient api, IJSRuntime js)
{
    public const string DefaultCode = "en";

    private Dictionary<string, string> _active = new();
    private Dictionary<string, string> _english = new();

    public string CurrentCode { get; private set; } = DefaultCode;
    public IReadOnlyList<LocaleInfo> Available { get; private set; } = [];

    /// <summary>Raised after the language changes; the layout re-renders the app in response.</summary>
    public event Action? Changed;

    /// <summary>Translate a key. Falls back active → English → the key itself (a gap is visible, not blank).</summary>
    public string this[string key] => Lookup(key) ?? key;

    /// <summary>Translate a parameterized key and fill its <c>{0}</c>/<c>{1}</c>… placeholders.</summary>
    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, this[key], args);

    /// <summary>
    /// Translate a count-dependent key. Looks up "<paramref name="key"/>.<i>category</i>" (category = the
    /// plural class of <paramref name="count"/> for the current language), falling back to "key.other". The
    /// count is passed as <c>{0}</c>, any <paramref name="args"/> follow as <c>{1}</c>, <c>{2}</c>…
    /// </summary>
    public string Plural(string key, int count, params object?[] args)
    {
        var category = PluralCategory(CurrentCode, count);
        var template = Lookup($"{key}.{category}") ?? Lookup($"{key}.other") ?? key;
        var all = new object?[args.Length + 1];
        all[0] = count;
        Array.Copy(args, 0, all, 1, args.Length);
        return string.Format(CultureInfo.CurrentCulture, template, all);
    }

    /// <summary>Load the language list, the English fallback, and the device's saved language, at boot.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            Available = await api.GetLocalesAsync();
            _english = (await api.GetLocaleAsync(DefaultCode))?.Strings ?? [];
            var code = await api.GetLanguageAsync() ?? DefaultCode;
            await ApplyAsync(code);
        }
        catch
        {
            // A boot-time fetch hiccup must never wedge the panel; the indexer falls back to the key.
        }
    }

    /// <summary>Switch language at runtime: persist the choice, load its strings, re-render the app.</summary>
    public async Task SetLanguageAsync(string code)
    {
        await api.SetLanguageAsync(code);
        await ApplyAsync(code);
        Changed?.Invoke();
    }

    // Point the active table at English (shared with the fallback) or at a loaded language, then reflect the
    // choice in <html lang> for accessibility/hyphenation. Falls back to English if the language won't load.
    private async Task ApplyAsync(string code)
    {
        if (code.Equals(DefaultCode, StringComparison.OrdinalIgnoreCase))
        {
            CurrentCode = DefaultCode;
            _active = _english;
        }
        else if (await api.GetLocaleAsync(code) is { } doc)
        {
            CurrentCode = doc.Code;
            _active = doc.Strings ?? [];
        }
        else
        {
            CurrentCode = DefaultCode;
            _active = _english;
        }
        await SetHtmlLangAsync(CurrentCode);
    }

    private string? Lookup(string key) =>
        _active.TryGetValue(key, out var v) ? v :
        _english.TryGetValue(key, out var e) ? e : null;

    private async Task SetHtmlLangAsync(string code)
    {
        try { await js.InvokeVoidAsync("document.documentElement.setAttribute", "lang", code); }
        catch { /* no DOM (pre-render) — ignore */ }
    }

    // CLDR plural categories for whole-number counts. English: one/other. Polish: one/few/many.
    // Unknown languages use the English rule; a translation can still supply only ".other".
    private static string PluralCategory(string code, int count)
    {
        var n = Math.Abs(count);
        if (code.StartsWith("pl", StringComparison.OrdinalIgnoreCase))
        {
            if (n == 1) return "one";
            var mod10 = n % 10;
            var mod100 = n % 100;
            if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return "few";
            return "many"; // 0, 5–21, and the 12–14 exceptions
        }
        return n == 1 ? "one" : "other";
    }
}
