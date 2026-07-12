using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using RpgSceneMaker.Api.Contracts;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Serves the panel's UI translations. Each language is a plain JSON file on disk — one per BCP-47 code
/// (<c>en.json</c>, <c>pl.json</c>, …) in the locales directory — so the community or an AI agent can add
/// or edit a translation by dropping/editing a file, no rebuild. Files are read on demand (they are small
/// and this is a LAN app), so an edit shows up on the next language switch or reload.
///
/// English (and Polish) are also shipped <b>embedded in this assembly</b> as the canonical key set. On
/// startup <see cref="Seed"/> copies the shipped files into the on-disk directory, but only the ones that
/// are missing — it never overwrites a community edit.
///
/// For a code that ships embedded, <see cref="Get"/> <b>merges</b> the two: the embedded strings are the
/// base and the on-disk strings are overlaid per key (disk wins key-by-key). This keeps community/manual
/// edits while guaranteeing newly shipped keys always appear even when the on-disk file was seeded by an
/// older build — so a stale file can never hide a new key (nor blank the UI if it is missing/broken). The
/// document's name metadata likewise prefers the on-disk values, falling back to embedded. Codes with no
/// embedded counterpart (community languages) are served from disk as-is.
/// </summary>
public partial class LocaleService(string directory, ILogger<LocaleService> logger)
{
    public const string DefaultCode = "en";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // A BCP-47-ish code: a bare file name, so Path.Combine can never be talked into traversing directories.
    [GeneratedRegex(@"^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})*$")]
    private static partial Regex CodePattern();

    // The on-disk file layout, minus the code (which comes from the file name).
    private record LocaleFile(string? Name, string? EnglishName, Dictionary<string, string>? Strings);

    /// <summary>Copy every shipped translation that is not already on disk into the locales directory.</summary>
    public void Seed()
    {
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var (code, resource) in EmbeddedResources())
            {
                var dest = Path.Combine(directory, code + ".json");
                if (File.Exists(dest)) continue; // never clobber a community edit
                using var stream = typeof(LocaleService).Assembly.GetManifestResourceStream(resource)!;
                using var file = File.Create(dest);
                stream.CopyTo(file);
                logger.LogInformation("Seeded locale file {Code} into {Dir}", code, directory);
            }
        }
        catch (Exception ex)
        {
            // Seeding is best-effort; a read-only dir just means the embedded copies remain the fallback.
            logger.LogWarning(ex, "Could not seed locale files into {Dir}", directory);
        }
    }

    /// <summary>Languages the panel can switch to: on-disk files, plus any shipped code missing from disk.</summary>
    public IReadOnlyList<LocaleInfo> List()
    {
        var infos = new Dictionary<string, LocaleInfo>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(path);
                if (!CodePattern().IsMatch(code)) continue;
                if (ReadFile(path) is { } file)
                    infos[code] = ToInfo(code, file);
            }
        }

        // Guarantee the shipped languages appear even if a user deleted their on-disk file.
        foreach (var (code, resource) in EmbeddedResources())
            if (!infos.ContainsKey(code) && ReadEmbedded(resource) is { } file)
                infos[code] = ToInfo(code, file);

        // English first, then the rest alphabetically by English name.
        return [.. infos.Values
            .OrderBy(i => i.Code.Equals(DefaultCode, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(i => i.EnglishName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// One language's document, or null if the code is unknown. For codes that ship embedded, the embedded
    /// strings are the base and the on-disk strings are overlaid per key (disk wins) so newly shipped keys
    /// always appear and community edits are preserved; a missing/broken on-disk file leaves embedded only.
    /// Codes with no embedded counterpart are served from disk as-is.
    /// </summary>
    public LocaleDocument? Get(string code)
    {
        if (!CodePattern().IsMatch(code)) return null;

        var path = Path.Combine(directory, code + ".json");
        var disk = File.Exists(path) ? ReadFile(path) : null;

        var embedded = EmbeddedResources().FirstOrDefault(r =>
            r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { Resource: { } res }
            ? ReadEmbedded(res)
            : null;

        // Community language (no embedded counterpart): serve the on-disk file as-is.
        if (embedded is null) return disk is null ? null : ToDocument(code, disk);

        // Shipped language: embedded is the base, on-disk overlays per key. This is what keeps a stale
        // on-disk file (seeded by an older build) from hiding keys shipped later, while preserving edits.
        var strings = new Dictionary<string, string>(embedded.Strings ?? [], StringComparer.Ordinal);
        if (disk?.Strings is { } overlay)
            foreach (var (key, value) in overlay)
                strings[key] = value;

        return new LocaleDocument(
            code,
            Pick(disk?.Name, embedded.Name, code),
            Pick(disk?.EnglishName, embedded.EnglishName, code),
            strings);
    }

    // Prefer the on-disk value, else the embedded value, else the code.
    private static string Pick(string? disk, string? embedded, string code) =>
        !string.IsNullOrWhiteSpace(disk) ? disk
        : !string.IsNullOrWhiteSpace(embedded) ? embedded
        : code;

    private LocaleFile? ReadFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<LocaleFile>(stream, Json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ignoring malformed locale file {Path}", path);
            return null;
        }
    }

    private LocaleFile? ReadEmbedded(string resource)
    {
        using var stream = typeof(LocaleService).Assembly.GetManifestResourceStream(resource);
        return stream is null ? null : JsonSerializer.Deserialize<LocaleFile>(stream, Json);
    }

    // The shipped translations embedded in this assembly, as (code, manifest-resource-name) pairs.
    private static IEnumerable<(string Code, string Resource)> EmbeddedResources()
    {
        const string marker = ".Locales.";
        foreach (var name in typeof(LocaleService).Assembly.GetManifestResourceNames())
        {
            var at = name.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0 || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var code = name[(at + marker.Length)..^".json".Length];
            yield return (code, name);
        }
    }

    private static LocaleInfo ToInfo(string code, LocaleFile file) =>
        new(code, string.IsNullOrWhiteSpace(file.Name) ? code : file.Name,
            string.IsNullOrWhiteSpace(file.EnglishName) ? code : file.EnglishName);

    private static LocaleDocument ToDocument(string code, LocaleFile file) =>
        new(code, string.IsNullOrWhiteSpace(file.Name) ? code : file.Name,
            string.IsNullOrWhiteSpace(file.EnglishName) ? code : file.EnglishName,
            file.Strings ?? []);
}
