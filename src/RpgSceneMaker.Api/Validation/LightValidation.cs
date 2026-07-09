namespace RpgSceneMaker.Api.Validation;

/// <summary>Shared light-related validation helpers.</summary>
public static class LightValidation
{
    public static bool IsSlug(string s) => s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

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
