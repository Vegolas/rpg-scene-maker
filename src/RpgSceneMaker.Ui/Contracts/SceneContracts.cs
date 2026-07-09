namespace RpgSceneMaker.Ui.Contracts;

// Wire DTOs mirroring the API's scene/light/music shapes. Contracts are duplicated per project by
// design (there is no shared contracts project) — keep these in sync with the API by hand.
public record SceneDto(string Id, string Name, LightDto? Light, List<SceneLightDto>? Lights, MusicDto? Music, List<string>? SoundEffects);
public record LightDto(bool? Power, string? Color, int? Brightness, int? Temperature);
public record SceneLightDto(string LightKey, bool? Power, int? Brightness, string? Color, int? Temperature, EffectDto? Effect);
public record EffectDto(string Type, int Speed, int Intensity, List<string>? Colors);
public record MusicDto(string? PlayId, double? Volume, bool Pause);
public record RegisteredLightInfo(string Key, string Name, string Provider);
public record ActivationDto(string Scene, string Light, string Music, bool FullySucceeded);
public record ActiveSceneDto(string? Id, DateTimeOffset? ActivatedAt);
