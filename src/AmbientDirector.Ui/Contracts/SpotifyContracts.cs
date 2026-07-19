namespace AmbientDirector.Ui.Contracts;

public record SpotifyDeviceDto(string Id, string Name, string Type, bool IsActive);
public record SpotifyPlaylistDto(string Id, string Name, string Uri, string? ImageUrl, int TrackCount);
public record SpotifyTrackDto(string Id, string Name, string Artist, string Uri, string? ImageUrl);
// Playback state moved to MusicContracts.MusicStateDto — /music/state is source-aware now.

// Mutable class — the settings form binds the Client ID input straight to it.
public class SpotifyConfigDto
{
    public string ClientId { get; set; } = "";
    public bool Connected { get; set; }
    public string PreferredDeviceId { get; set; } = "";
    public string PreferredDeviceName { get; set; } = "";
    public string RedirectUri { get; set; } = "";
}
