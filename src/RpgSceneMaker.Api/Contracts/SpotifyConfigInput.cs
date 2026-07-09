namespace RpgSceneMaker.Api.Contracts;

// Body of PUT /setup/spotify/config (device fields optional — omitted keeps the saved value).
public record SpotifyConfigInput(string ClientId, string? PreferredDeviceId, string? PreferredDeviceName);
