namespace AmbientDirector.Ui.Contracts;

// UI copy of the GET /setup/onboarding wire shape (duplicated by hand per the project's duplicated-DTO
// convention — see the API's SetupEndpoints.cs).

/// <summary>First-run onboarding state: whether the wizard should show, plus per-step "already done"
/// flags so a step can pre-mark itself (e.g. Spotify connected before the wizard ever ran).</summary>
public record OnboardingDto(
    bool Show,
    bool LightsConfigured,
    bool SpotifyConnected,
    bool LocalMusicAvailable,
    bool AssistantConfigured,
    bool FreesoundConfigured);
