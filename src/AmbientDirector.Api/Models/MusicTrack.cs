namespace AmbientDirector.Api.Models;

/// <summary>A bring-your-own music file in the local library: an audio file on disk plus a little metadata.
/// Played on the server's own audio device by <c>LocalMusicPlayer</c> (the local <c>IMusicSource</c>),
/// referenced by a scene as <c>local:track:{id}</c>. The sibling of a Spotify track for the offline table.</summary>
public class MusicTrack
{
    /// <summary>Slug id (matched case-insensitively, like sounds) used in <c>/music/library/tracks/{id}</c>
    /// URLs and in the self-describing <c>local:track:{id}</c> play id.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>File name (not full path) under the music directory, e.g. "tavern.mp3". Resolved via <c>MusicFileStorage</c>.</summary>
    public string FileName { get; set; } = "";

    /// <summary>The file's natural length in milliseconds, measured at import (same reader logic as the
    /// soundboard). Null means "not measured yet" (backfilled lazily by the tracks list); <c>0</c> is the
    /// "tried, unmeasurable" sentinel persisted when the file won't decode, so the backfill doesn't re-probe
    /// it forever — consumers treat any value <c>&lt;= 0</c> as an unknown length.</summary>
    public int? DurationMs { get; set; }

    /// <summary>Optional free-text artist / attribution shown next to the track. Never required.</summary>
    public string Artist { get; set; } = "";
}
