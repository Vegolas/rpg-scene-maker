namespace RpgSceneMaker.Api.Models;

/// <summary>An imported sound effect: an audio file on disk plus playback tuning. Played on the
/// server's own audio device by <c>SoundboardPlayer</c>, ad-hoc from the panel or fired by a scene.</summary>
public class Sound
{
    /// <summary>Slug id (matched case-insensitively, like scenes) used in <c>/sounds/{id}/…</c> URLs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Free-text group the panel uses to lay sounds out in sections (e.g. "Ambience", "Combat").</summary>
    public string Category { get; set; } = "";

    /// <summary>File name (not full path) under the sounds directory, e.g. "thunder.mp3". Resolved via <c>SoundFileStorage</c>.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Playback volume 0.0 - 1.0.</summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>When true the sound loops until stopped; otherwise it plays once.</summary>
    public bool Loop { get; set; }

    /// <summary>Stored file name of an optional full-art tile background (uploaded via <c>/images</c>), or null.</summary>
    public string? Image { get; set; }

    /// <summary>The file's natural length in milliseconds, measured at import. Null means "not measured
    /// yet" (predates duration tracking; backfilled lazily by <c>/sounds/list</c>). <c>0</c> is the
    /// "tried, unmeasurable" sentinel persisted when the file won't decode, so the backfill doesn't
    /// re-probe it forever — consumers treat any value <c>&lt;= 0</c> as an unknown length.</summary>
    public int? DurationMs { get; set; }

    /// <summary>Compact amplitude preview of the file (<see cref="SoundboardPlayer.WaveformBuckets"/> peaks,
    /// each 0–255, normalized to the loudest sample), drawn as the waveform on timeline sound clips. Measured
    /// at import alongside <see cref="DurationMs"/>. Null means "not computed yet" (predates the feature;
    /// backfilled lazily by <c>/sounds/list</c>); an empty array is the "tried, unmeasurable" sentinel so the
    /// backfill doesn't re-probe a file that won't decode.</summary>
    public byte[]? Waveform { get; set; }

    // ---- Attribution (set only by a library import, e.g. Freesound; null for a plain file upload) ----
    // These are server-set at import and NOT editable via PUT /sounds/{id}.

    /// <summary>Original author/uploader of an imported sound (Freesound "username"), or null.</summary>
    public string? Author { get; set; }

    /// <summary>Short license label for an imported sound (e.g. "CC BY 4.0", "CC0 1.0"), or null.</summary>
    public string? License { get; set; }

    /// <summary>Canonical license deed URL for an imported sound (a Creative Commons link), or null.</summary>
    public string? LicenseUrl { get; set; }

    /// <summary>Link back to the source page the sound was imported from (e.g. https://freesound.org/s/{id}/), or null.</summary>
    public string? SourceUrl { get; set; }
}
