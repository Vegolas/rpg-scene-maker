namespace RpgSceneMaker.Ui.Contracts;

// Mirrors the API's DiagnosticsDto (Contracts are duplicated, not shared — keep in sync by hand).
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
