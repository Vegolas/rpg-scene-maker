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
}
