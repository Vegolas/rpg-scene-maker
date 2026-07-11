using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Shared light-related validation helpers.</summary>
public static class LightValidation
{
    public static bool IsSlug(string s) => s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Validate a light effect and normalize its colors in place. Shared by scene per-light
    /// effects and event timeline light clips. <paramref name="context"/> names the owner in messages.</summary>
    public static void ValidateEffect(LightEffect fx, string context)
    {
        fx.Colors ??= [];
        if (fx.Type is not ("flicker" or "glow" or "storm" or "drift"))
            throw new ArgumentException($"Unknown effect type '{fx.Type}' on {context}. Use flicker, glow, storm or drift.");
        if (fx.Speed is < 1 or > 10)
            throw new ArgumentException($"Effect speed on {context} must be between 1 and 10.");
        if (fx.Intensity is < 1 or > 10)
            throw new ArgumentException($"Effect intensity on {context} must be between 1 and 10.");
        for (var i = 0; i < fx.Colors.Count; i++)
            fx.Colors[i] = NormalizeHex(fx.Colors[i]);
        if (fx.Type == "drift" && fx.Colors.Count < 2)
            throw new ArgumentException($"The 'drift' effect on {context} needs at least 2 colors.");
    }

    // Accept #RGB or #RRGGBB (leading # optional) and store the canonical "#RRGGBB" the light services parse.
    public static string NormalizeHex(string raw)
    {
        var s = raw.Trim().TrimStart('#');
        if (s.Length == 3 && s.All(Uri.IsHexDigit))
            s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length != 6 || !s.All(Uri.IsHexDigit))
            throw new ArgumentException($"'{raw}' is not a valid hex color. Use #RGB or #RRGGBB, e.g. #FF8C2A.");
        return "#" + s.ToUpperInvariant();
    }
}
