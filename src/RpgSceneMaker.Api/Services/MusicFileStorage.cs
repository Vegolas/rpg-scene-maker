using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>Stores each local music track's audio file on disk (one file per track) and resolves its path.
/// The music sibling of <see cref="SoundFileStorage"/>; sits under <c>%LocalAppData%\RpgSceneMaker\music</c>
/// by default (override <c>Music:Path</c>).</summary>
public class MusicFileStorage
{
    /// <summary>File types we can decode — the same set as the soundboard (WAV/MP3 natively, OGG via
    /// NAudio.Vorbis), so a track and a sound accept identical uploads.</summary>
    public static readonly string[] AllowedExtensions = SoundFileStorage.AllowedExtensions;

    private readonly string _directory;

    public MusicFileStorage(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Absolute path to a track's audio file, for the player.</summary>
    public string FullPath(MusicTrack track) => Path.Combine(_directory, track.FileName);

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
