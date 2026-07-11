using System.Text.RegularExpressions;

namespace RpgSceneMaker.Api.Services;

/// <summary>Stores each entity's optional full-art tile background on disk (one file per image) and
/// resolves its path. Sits next to the SQLite database under <c>%LocalAppData%\RpgSceneMaker\images</c>
/// by default. Mirrors <see cref="SoundFileStorage"/>. Images arrive already cropped + downscaled by the
/// browser; entities reference their image by stored file name only (never a path or URL).</summary>
public class ImageFileStorage
{
    /// <summary>Tile-art file types we accept (browser encodes to one of these before upload).</summary>
    public static readonly string[] AllowedExtensions = [".webp", ".jpg", ".jpeg", ".png"];

    // Stored names are "&lt;lowercase-slug&gt;.&lt;ext&gt;" — ids are generated lowercase, so the guard is
    // lowercase-only. Doubles as the path-traversal guard (no '..', slashes or drive letters can match).
    private static readonly Regex ValidName =
        new(@"^[a-z0-9-]+\.(webp|jpe?g|png)$", RegexOptions.Compiled);

    private readonly string _directory;

    public ImageFileStorage(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Persist an uploaded stream as "&lt;id&gt;&lt;ext&gt;" and return the stored file name.</summary>
    public async Task<string> SaveAsync(string id, string extension, Stream content, CancellationToken ct = default)
    {
        var fileName = id + extension.ToLowerInvariant();
        await using var file = File.Create(Path.Combine(_directory, fileName));
        await content.CopyToAsync(file, ct);
        return fileName;
    }

    /// <summary>Best-effort delete of a stored image; swallows IO errors and no-ops on a missing/blank name.</summary>
    public void Delete(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        try
        {
            var full = Path.Combine(_directory, fileName);
            if (File.Exists(full)) File.Delete(full);
        }
        catch (IOException) { /* stale/locked file — a leftover on disk is harmless */ }
        catch (UnauthorizedAccessException) { /* ditto */ }
    }

    /// <summary>True only for a safe stored name ("slug.ext"); the guard against traversal/junk refs.</summary>
    public static bool IsValidName(string? name) => name is not null && ValidName.IsMatch(name);

    /// <summary>Absolute path for a stored name, or null if the name is unsafe (traversal, slashes, bad ext).</summary>
    public string? FullPathForName(string name) =>
        IsValidName(name) ? Path.Combine(_directory, name) : null;

    /// <summary>Content-type for a stored file name or a bare extension (with or without the leading dot).</summary>
    public static string ContentTypeFor(string fileNameOrExt)
    {
        var ext = Path.GetExtension(fileNameOrExt);
        if (string.IsNullOrEmpty(ext)) ext = fileNameOrExt; // caller passed a bare "webp"/".webp"
        return ext.TrimStart('.').ToLowerInvariant() switch
        {
            "webp" => "image/webp",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            _ => "application/octet-stream",
        };
    }
}
