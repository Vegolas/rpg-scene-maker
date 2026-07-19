namespace AmbientDirector.Api.Contracts;

// Read-only runtime facts surfaced by GET /diagnostics for the panel's developer mode.
public record DiagnosticsDto(
    string Version,
    string Environment,
    DateTimeOffset StartedAt,
    string LightProvider,
    bool SpotifyConnected,
    bool SoundboardSupported,
    int PlayingSoundCount,
    int SceneCount,
    int SoundCount,
    int EventCount,
    string DatabasePath,
    string SoundsPath);
