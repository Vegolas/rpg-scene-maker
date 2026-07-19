using System.ComponentModel.DataAnnotations.Schema;

namespace AmbientDirector.Api.Data;

/// <summary>
/// Spotify connection state, stored as a single row (Id = 1) in SQLite.
/// The Client ID comes from the Spotify developer dashboard; the refresh token is obtained
/// via the Authorization Code + PKCE flow and used to mint short-lived access tokens.
/// </summary>
public class SpotifyConfig
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>App Client ID from developer.spotify.com. Empty until the user configures it.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Long-lived refresh token from the PKCE flow. Empty until the user connects.</summary>
    public string RefreshToken { get; set; } = "";

    /// <summary>Spotify Connect device id to target for playback (empty = whatever is active).</summary>
    public string PreferredDeviceId { get; set; } = "";

    /// <summary>Human-readable name of the preferred device, shown in the UI.</summary>
    public string PreferredDeviceName { get; set; } = "";

    [NotMapped]
    public bool IsConfigured => ClientId != "";

    [NotMapped]
    public bool IsConnected => IsConfigured && RefreshToken != "";
}
