using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

/// <summary>Stores each sound effect's audio file on disk (one file per sound) and resolves its path.
/// Sits next to the SQLite database under <c>%LocalAppData%\AmbientDirector\sounds</c> by default.</summary>
public class SoundFileStorage
{
    /// <summary>File types we can decode (see <c>SoundDecoder</c>: managed WAV, MP3 via NLayer, OGG via NVorbis — all cross-platform).</summary>
    public static readonly string[] AllowedExtensions = [".mp3", ".wav", ".ogg"];

    private readonly string _directory;

    public SoundFileStorage(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Absolute path to a sound's audio file, for the player.</summary>
    public string FullPath(Sound sound) => Path.Combine(_directory, sound.FileName);

    /// <summary>Persist an uploaded stream as "&lt;id&gt;&lt;ext&gt;" and return the stored file name.</summary>
    public async Task<string> SaveAsync(string id, string extension, Stream content, CancellationToken ct = default)
    {
        var fileName = id + extension.ToLowerInvariant();
        await using var file = File.Create(Path.Combine(_directory, fileName));
        await content.CopyToAsync(file, ct);
        return fileName;
    }

    public void Delete(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        var full = Path.Combine(_directory, fileName);
        if (File.Exists(full)) File.Delete(full);
    }
}
