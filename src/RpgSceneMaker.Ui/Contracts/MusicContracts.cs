namespace RpgSceneMaker.Ui.Contracts;

// Wire DTOs mirroring the API's source-aware music shapes (Contracts/MusicContracts.cs there) — duplicated
// by hand per project, like the rest of Contracts/.

/// <summary>GET /music/state: the neutral playback state plus which source produced it (null when nothing is
/// active) and which sources are usable right now ("spotify" when connected; "local" when the library has a
/// track or something is loaded). An empty <c>Available</c> hides the transport (QuickControls + Music tab).</summary>
public record MusicStateDto(string? Source, List<string>? Available, bool IsPlaying, string? TrackName,
    string? ArtistName, string? ContextName, string? DeviceName, int? VolumePercent,
    double? ProgressSeconds, double? DurationSeconds, bool IsShuffling, string Repeat);

/// <summary>A local music-library track; <c>PlayId</c> is its ready-to-use <c>local:track:{id}</c> reference.</summary>
public record MusicTrackDto(string Id, string Name, string Artist, int? DurationMs, string PlayId);

/// <summary>A local music-library playlist (ordered track ids) with its <c>local:playlist:{id}</c> reference.</summary>
public record MusicPlaylistDto(string Id, string Name, List<string> TrackIds, string PlayId);
