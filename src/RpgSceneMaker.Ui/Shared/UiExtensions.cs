using Microsoft.AspNetCore.Components;
using RpgSceneMaker.Ui.Contracts;

namespace RpgSceneMaker.Ui.Shared;

public static class UiExtensions
{
    /// <summary>Parse an input's value as an int, or null when it isn't a number.</summary>
    public static int? AsInt(this ChangeEventArgs e) =>
        int.TryParse(e.Value?.ToString(), out var v) ? v : null;

    /// <summary>"light: … | music: … | sound: …" from an activation result's failed parts (the "error:" prefix stripped).</summary>
    public static string ProblemSummary(this ActivationDto result) =>
        Summarize(("light", result.Light), ("music", result.Music), ("sound", result.SoundEffects));

    /// <summary>"light: … | sound: …" from an event trigger's failed parts (the "error:" prefix stripped).</summary>
    public static string ProblemSummary(this EventTriggerDto result) =>
        Summarize(("light", result.Light), ("sound", result.Sound));

    private static string Summarize(params (string Label, string Status)[] parts) =>
        string.Join(" | ", parts
            .Where(p => p.Status.StartsWith("error"))
            .Select(p => $"{p.Label}: {p.Status[7..]}"));
}
