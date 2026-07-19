namespace AmbientDirector.Api.Services.Music;

/// <summary>
/// Provider-neutral playback state returned by an <see cref="IMusicSource"/>. Kept deliberately close to the
/// Spotify wire shape (<c>SpotifyPlaybackState</c>) so the panel's DTO barely changes: the only real
/// additions are <see cref="Source"/> (which source produced this) and the rename of the raw Spotify context
/// URI to a friendly <see cref="ContextName"/> (a playlist name for local; null for Spotify, whose context is
/// only a URI). <see cref="DeviceName"/>/<see cref="ProgressSeconds"/>/<see cref="DurationSeconds"/> are
/// best-effort: Spotify fills them all, local fills progress/duration and leaves the device null.
/// </summary>
public record MusicState(
    string Source,
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
