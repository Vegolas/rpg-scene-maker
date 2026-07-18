namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Maps a Freesound license URL (the raw <c>license</c> field on a sound, a Creative Commons deed link) to a
/// short human-readable label like "CC BY 4.0" / "CC0". Freesound only issues a small, fixed set of licenses;
/// an unrecognized URL is returned verbatim so nothing is lost. Matching is scheme- and trailing-slash-
/// insensitive so both <c>http://…/by/4.0/</c> and <c>https://…/by/4.0</c> resolve.
/// </summary>
public static class FreesoundLicense
{
    // Normalized deed path (scheme dropped, lower-cased, no trailing slash) → short label.
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["creativecommons.org/publicdomain/zero/1.0"] = "CC0 1.0",
        ["creativecommons.org/licenses/by/4.0"] = "CC BY 4.0",
        ["creativecommons.org/licenses/by/3.0"] = "CC BY 3.0",
        ["creativecommons.org/licenses/by-nc/4.0"] = "CC BY-NC 4.0",
        ["creativecommons.org/licenses/by-nc/3.0"] = "CC BY-NC 3.0",
        ["creativecommons.org/licenses/sampling+/1.0"] = "Sampling+",
    };

    /// <summary>The short label for a Freesound license URL. Null/blank → "" (no license captured); a URL we
    /// don't recognize is returned trimmed, as-is, so it can still be shown/linked.</summary>
    public static string Label(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var trimmed = url.Trim();

        var key = Normalize(trimmed);
        if (Labels.TryGetValue(key, out var label)) return label;

        // Resilience for version-less or variant deed links Freesound might emit.
        if (key.Contains("publicdomain/zero")) return "CC0 1.0";
        if (key.Contains("licenses/sampling+")) return "Sampling+";

        return trimmed;
    }

    // Drop the scheme, lower-case, and trim a single trailing slash so deed links compare canonically.
    private static string Normalize(string url)
    {
        var s = url.ToLowerInvariant();
        if (s.StartsWith("https://")) s = s[8..];
        else if (s.StartsWith("http://")) s = s[7..];
        return s.TrimEnd('/');
    }
}
