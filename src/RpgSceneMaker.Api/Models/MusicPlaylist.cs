namespace RpgSceneMaker.Api.Models;

/// <summary>An ordered list of local <see cref="MusicTrack"/>s, played as a queue by <c>LocalMusicPlayer</c>
/// and referenced by a scene as <c>local:playlist:{id}</c>. Purely a grouping — it owns no audio of its
/// own, just the ordered ids of tracks in the library.</summary>
public class MusicPlaylist
{
    /// <summary>Slug id (matched case-insensitively) used in <c>/music/library/playlists/{id}</c> URLs and in
    /// the self-describing <c>local:playlist:{id}</c> play id.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Ordered ids of the <see cref="MusicTrack"/>s in this playlist. Stored as one JSON column
    /// (a primitive collection, like <c>Scene.SoundEffects</c>). Ids that no longer resolve are skipped at
    /// play time and scrubbed when a track is deleted.</summary>
    public List<string> TrackIds { get; set; } = [];
}
