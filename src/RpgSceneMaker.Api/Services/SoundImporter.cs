using System.Text;
using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Services;

/// <summary>Attribution captured for a library-imported sound (Freesound). All null for a plain file upload.</summary>
public record SoundImportMeta(string? Author, string? License, string? LicenseUrl, string? SourceUrl);

/// <summary>
/// The shared tail of importing a sound, used by both the multipart upload (<c>/sounds/import</c>) and the
/// library import (<c>/sounds/library/import</c>): pick a unique slug id, save the audio file, measure its
/// natural length + waveform preview, attach optional attribution, validate and upsert. Kept in one place so
/// the two entry points behave identically (the multipart path passes no <see cref="SoundImportMeta"/>, so it
/// is byte-for-byte the previous inline logic).
/// </summary>
public class SoundImporter(SoundStore store, SoundFileStorage files)
{
    /// <summary>Import an audio stream as a new sound and return the stored entity. <paramref name="extension"/>
    /// includes the leading dot (e.g. ".mp3"); <paramref name="meta"/> carries library attribution (null for a
    /// plain upload).</summary>
    public async Task<Sound> ImportAsync(string name, string category, string extension, Stream content,
        SoundImportMeta? meta = null, CancellationToken ct = default)
    {
        var id = await UniqueIdAsync(name);
        var storedName = await files.SaveAsync(id, extension, content, ct);
        var sound = new Sound { Id = id, Name = name, Category = category, FileName = storedName };

        // Measure the file's natural length + waveform preview now (same reader logic as playback); the
        // "unmeasurable" sentinels (0 / empty array) if it won't decode, so /sounds/list never re-probes it.
        var fullPath = files.FullPath(sound);
        sound.DurationMs = SoundboardPlayer.TryMeasureDurationMs(fullPath) ?? 0;
        sound.Waveform = SoundboardPlayer.TryComputeWaveform(fullPath) ?? [];

        // Server-set attribution for library imports (never editable via PUT /sounds/{id}).
        if (meta is not null)
        {
            sound.Author = Blank(meta.Author);
            sound.License = Blank(meta.License);
            sound.LicenseUrl = Blank(meta.LicenseUrl);
            sound.SourceUrl = Blank(meta.SourceUrl);
        }

        SoundValidation.Validate(sound);
        await store.UpsertAsync(sound);
        return sound;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Slugify the name, then suffix -2, -3, … until it's free (ids are the /sounds/{id}/… URL segment).
    private async Task<string> UniqueIdAsync(string name)
    {
        var baseSlug = Slugify(name);
        if (baseSlug.Length == 0) baseSlug = "sound";
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
