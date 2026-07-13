using Microsoft.AspNetCore.Components;
using RpgSceneMaker.Ui.Contracts;

namespace RpgSceneMaker.Ui.Shared;

public static class UiExtensions
{
    /// <summary>Parse an input's value as an int, or null when it isn't a number.</summary>
    public static int? AsInt(this ChangeEventArgs e) =>
        int.TryParse(e.Value?.ToString(), out var v) ? v : null;

    /// <summary>"light: … | music: … | sound: …" from an activation result's failed parts. Both the part
    /// label and the failure (a server error <em>code</em>) are localized via <paramref name="tr"/> — a key →
    /// string translator, e.g. the <c>Localizer</c> indexer <c>k =&gt; L[k]</c>.</summary>
    public static string ProblemSummary(this ActivationDto result, Func<string, string> tr) =>
        Summarize(tr, ("error.part.light", result.Light), ("error.part.music", result.Music),
            ("error.part.sound", result.SoundEffects));

    /// <summary>"light: … | sound: …" from an event trigger's failed parts, label + code localized via
    /// <paramref name="tr"/>.</summary>
    public static string ProblemSummary(this EventTriggerDto result, Func<string, string> tr) =>
        Summarize(tr, ("error.part.light", result.Light), ("error.part.sound", result.Sound));

    private static string Summarize(Func<string, string> tr, params (string LabelKey, string Status)[] parts) =>
        string.Join(" | ", parts
            .Where(p => p.Status.StartsWith("error"))
            .Select(p => $"{tr(p.LabelKey)}: {tr(Tail(p.Status))}"));

    // The status is "error:<code>" (a locale key naming the failure, e.g. error.title.bulbUnreachable); take
    // the tail after the first ':' so the caller can translate it.
    private static string Tail(string status)
    {
        var colon = status.IndexOf(':');
        return colon >= 0 ? status[(colon + 1)..] : status;
    }
}
