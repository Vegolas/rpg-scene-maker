namespace RpgSceneMaker.Api.Contracts;

// Wire DTOs for GET/PUT /setup/config — mapped to/from the EF entities by SettingsStore.
public record TuyaConfigDto(string Ip, string DeviceId, string LocalKey, string ProtocolVersion, string DpProfile);
public record HueConfigDto(string BridgeIp, string AppKey, List<string> LightIds);
public record RegisteredLightDto(string Key, string Name, string Provider, string? HueId);
// Lights is optional so older clients that don't send it keep working (treated as empty).
public record LightingConfigDto(string Provider, HueConfigDto Hue, TuyaConfigDto Tuya, List<RegisteredLightDto>? Lights = null);
