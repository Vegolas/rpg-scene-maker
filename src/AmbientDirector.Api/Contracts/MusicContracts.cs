namespace AmbientDirector.Api.Contracts;

/// <summary>Wire shape for <c>GET /music/state</c>: the provider-neutral playback state plus which source
/// produced it (<c>Source</c>, null when nothing is active) and which sources can be used right now
/// (<c>Available</c>, e.g. ["spotify","local"] or just ["local"]). The panel needs one call to drive the top
/// bar. Fields mirror the old Spotify state shape; <c>ContextName</c> replaces the raw context URI (a playlist
/// name for local, null for Spotify). <c>DeviceName</c>/<c>ProgressSeconds</c>/<c>DurationSeconds</c> are
/// best-effort (Spotify fills all; local leaves the device null).</summary>
public record MusicStateDto(
    string? Source,
    IReadOnlyList<string> Available,
    bool IsPlaying,
    string? TrackName,
    string? ArtistName,
    string? ContextName,
    string? DeviceName,
    int? VolumePercent,
    double? ProgressSeconds,
    double? DurationSeconds,
    bool IsShuffling,
    string Repeat);

/// <summary>A local music-library track. <c>PlayId</c> is the ready-to-use <c>local:track:{id}</c> reference
/// (kept server-authoritative so the panel/scenes don't hand-build the scheme); <c>DurationMs</c> is the
/// file's natural length (null when it can't be decoded).</summary>
public record MusicTrackDto(string Id, string Name, string Artist, int? DurationMs, string PlayId);

/// <summary>A local music-library playlist: an ordered list of track ids plus its <c>local:playlist:{id}</c>
/// <c>PlayId</c>.</summary>
public record MusicPlaylistDto(string Id, string Name, IReadOnlyList<string> TrackIds, string PlayId);

/// <summary>Editable fields for a local track (name/artist); each null field is left unchanged.</summary>
public record MusicTrackUpdateInput(string? Name, string? Artist);

/// <summary>Body of <c>PUT /music/library/playlists/{id}</c>: the playlist name and its ordered track ids
/// (the id comes from the URL).</summary>
public record MusicPlaylistInput(string Name, List<string>? TrackIds);
