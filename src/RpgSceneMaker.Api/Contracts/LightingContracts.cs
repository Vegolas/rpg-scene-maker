namespace RpgSceneMaker.Api.Contracts;

// Wire DTOs for GET/PUT /setup/config — mapped to/from the EF entities by SettingsStore.
public record TuyaConfigDto(string Ip, string DeviceId, string LocalKey, string ProtocolVersion, string DpProfile);
public record HueConfigDto(string BridgeIp, string AppKey, List<string> LightIds);
public record RegisteredLightDto(string Key, string Name, string Provider, string? HueId);
// The state /lights/default restores. Same shape as a scene's light block (Models.LightSettings).
public record DefaultLightDto(bool? Power, string? Color, int? Brightness, int? Temperature);
// Lights / DefaultLight are optional so older clients that don't send them keep working.
public record LightingConfigDto(
    string Provider, HueConfigDto Hue, TuyaConfigDto Tuya,
    List<RegisteredLightDto>? Lights = null, DefaultLightDto? DefaultLight = null);
