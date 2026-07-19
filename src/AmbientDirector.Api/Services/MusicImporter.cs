using System.Text;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services;

/// <summary>
/// The shared tail of importing a local music track (used by <c>POST /music/library/import</c>): pick a
/// unique slug id, save the audio file, measure its natural length, validate and upsert. The music sibling of
/// <see cref="SoundImporter"/>; it stays separate because a track is a different entity/store (and needs no
/// waveform), but it reuses the soundboard's static duration measurement so a track and a sound are timed the
/// same way.
/// </summary>
public class MusicImporter(MusicTrackStore store, MusicFileStorage files)
{
    /// <summary>Import an audio stream as a new track and return the stored entity. <paramref name="extension"/>
    /// includes the leading dot (e.g. ".mp3").</summary>
    public async Task<MusicTrack> ImportAsync(string name, string artist, string extension, Stream content,
        CancellationToken ct = default)
    {
        var id = await UniqueIdAsync(name);
        var storedName = await files.SaveAsync(id, extension, content, ct);
        var track = new MusicTrack { Id = id, Name = name, Artist = artist, FileName = storedName };

        // Measure the file's natural length now (same reader logic as playback); the "unmeasurable" sentinel
        // (0) if it won't decode, so the tracks list never re-probes it.
        track.DurationMs = SoundboardPlayer.TryMeasureDurationMs(files.FullPath(track)) ?? 0;

        MusicValidation.ValidateTrack(track);
        await store.UpsertAsync(track);
        return track;
    }

    // Slugify the name, then suffix -2, -3, … until it's free (ids are the /music/library/tracks/{id} segment).
    private async Task<string> UniqueIdAsync(string name)
    {
        var baseSlug = Slugify(name);
        if (baseSlug.Length == 0) baseSlug = "track";
        var id = baseSlug;
        for (var n = 2; await store.GetAsync(id) is not null; n++)
            id = $"{baseSlug}-{n}";
        return id;
    }

    private static string Slugify(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
