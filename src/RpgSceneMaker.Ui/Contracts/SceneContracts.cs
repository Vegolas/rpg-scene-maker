namespace RpgSceneMaker.Ui.Contracts;

// Wire DTOs mirroring the API's scene/light/music shapes. Contracts are duplicated per project by
// design (there is no shared contracts project) — keep these in sync with the API by hand.
public record SceneDto(string Id, string Name, LightDto? Light, List<SceneLightDto>? Lights, MusicDto? Music, List<string>? SoundEffects, string? Image);
public record LightDto(bool? Power, string? Color, int? Brightness, int? Temperature);
public record SceneLightDto(string LightKey, bool? Power, int? Brightness, string? Color, int? Temperature, EffectDto? Effect);
public record EffectDto(string Type, int Speed, int Intensity, List<string>? Colors,
    List<KeyframeDto>? Keyframes = null, bool Loop = false, int? CycleMs = null, string? FxId = null);
// One keyframe of a "custom" effect: a light state at a ms offset, with an optional Hue transition.
public record KeyframeDto(int AtMs, bool? Power, string? Color, int? Brightness, int? Temperature, int? TransitionMs);
// Source is "spotify" | "local" | null (legacy — inferred from the PlayId shape server-side).
public record MusicDto(string? Source, string? PlayId, double? Volume, bool Pause);
public record RegisteredLightInfo(string Key, string Name, string Provider);
// Normalized live bulb state from GET /lights/status (Raw provider payload omitted — the panel only reflects the normalized fields).
public record LightStatusDto(bool? On, string? Mode, int? Brightness, string? Color, int? Temperature);
public record ActivationDto(string Scene, string Light, string Music, string SoundEffects, bool FullySucceeded);
public record ActiveSceneDto(string? Id, DateTimeOffset? ActivatedAt);
