using System.Text;

namespace RpgSceneMaker.Ui.Shared;

/// <summary>Scene name / id string helpers shared by the Scenes list, the editor and Settings.</summary>
public static class SceneNaming
{
    // "🍺 Tavern" -> ("🍺", "Tavern"). A name with no leading symbol yields (emojiFallback, name);
    // a blank name falls back to labelFallback (e.g. the scene id) for the label.
    public static (string Emoji, string Label) SplitName(string name, string emojiFallback = "", string? labelFallback = null)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? labelFallback ?? "" : name.Trim();
        var space = trimmed.IndexOf(' ');
        if (space > 0 && trimmed[..space].Any(c => c > (char)0x2000))
            return (trimmed[..space], trimmed[(space + 1)..]);
        return (emojiFallback, trimmed);
    }

    // Lower-case ASCII slug: letters/digits kept, runs of space/-/_ collapse to a single '-'.
    public static string Slugify(string label)
    {
        var sb = new StringBuilder();
        foreach (var c in label.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-' or '_' && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    // Append -2, -3, … until the id/key is unique (case-insensitive) among the existing ones.
    public static string MakeUnique(string baseId, IReadOnlyCollection<string> existing, string fallback = "")
    {
        if (string.IsNullOrEmpty(baseId)) baseId = fallback;
        if (string.IsNullOrEmpty(baseId)) return "";
        var id = baseId;
        var n = 2;
        while (existing.Contains(id, StringComparer.OrdinalIgnoreCase))
            id = $"{baseId}-{n++}";
        return id;
    }
}
