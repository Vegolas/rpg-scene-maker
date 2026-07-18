namespace RpgSceneMaker.Api.Services.Music;

/// <summary>
/// The self-describing local play-id scheme: <c>local:track:{id}</c> and <c>local:playlist:{id}</c>. The
/// mirror of a <c>spotify:</c> URI — a scene's <c>MusicSettings.PlayId</c> (or <c>/music/play?id=</c>) carries
/// the whole reference, so the router can infer the source from the id shape alone.
/// </summary>
public static class LocalMusicId
{
    private const string TrackPrefix = "local:track:";
    private const string PlaylistPrefix = "local:playlist:";

    /// <summary>True for any <c>local:</c> id (used by source inference / scene validation).</summary>
    public static bool IsLocal(string id) =>
        id is not null && id.StartsWith("local:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Build the play id for a library track.</summary>
    public static string ForTrack(string trackId) => TrackPrefix + trackId;

    /// <summary>Build the play id for a library playlist.</summary>
    public static string ForPlaylist(string playlistId) => PlaylistPrefix + playlistId;

    /// <summary>Parse a <c>local:</c> id into its kind ("track" | "playlist") and entity id. Returns false for
    /// a non-local id, an unknown kind, or an empty entity id.</summary>
    public static bool TryParse(string id, out string kind, out string entityId)
    {
        kind = "";
        entityId = "";
        if (string.IsNullOrWhiteSpace(id)) return false;
        id = id.Trim();

        if (id.StartsWith(TrackPrefix, StringComparison.OrdinalIgnoreCase))
        {
            entityId = id[TrackPrefix.Length..];
            kind = "track";
        }
        else if (id.StartsWith(PlaylistPrefix, StringComparison.OrdinalIgnoreCase))
        {
            entityId = id[PlaylistPrefix.Length..];
            kind = "playlist";
        }
        else
        {
            return false;
        }

        return entityId.Length > 0;
    }
}
