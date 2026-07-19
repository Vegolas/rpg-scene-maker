namespace AmbientDirector.Api.Services;

// Result shapes returned by SpotifyClient and serialised straight to /music and /setup/spotify callers.
public record SpotifyDevice(string Id, string Name, string Type, bool IsActive);
public record SpotifyPlaylist(string Id, string Name, string Uri, string? ImageUrl, int TrackCount);
public record SpotifyTrack(string Id, string Name, string Artist, string Uri, string? ImageUrl);
public record SpotifyPlaybackState(
    bool IsPlaying, string? TrackName, string? ArtistName, string? ContextUri, string? DeviceName, int? VolumePercent,
    double? ProgressSeconds, double? DurationSeconds, bool IsShuffling, string Repeat);
