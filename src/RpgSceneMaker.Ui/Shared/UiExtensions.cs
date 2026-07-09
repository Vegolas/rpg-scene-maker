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
        string.Join(" | ", new[] { ("light", result.Light), ("music", result.Music), ("sound", result.SoundEffects) }
            .Where(p => p.Item2.StartsWith("error"))
            .Select(p => $"{p.Item1}: {p.Item2[7..]}"));
}
