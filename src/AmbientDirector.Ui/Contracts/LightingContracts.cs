namespace AmbientDirector.Ui.Contracts;

// Mutable classes (not records) — the settings form binds inputs straight to them.
public class TuyaConfigDto
{
    public string Ip { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string LocalKey { get; set; } = "";
    public string ProtocolVersion { get; set; } = "3.3";
    public string DpProfile { get; set; } = "v2";
}

public class HueConfigDto
{
    public string BridgeIp { get; set; } = "";
    public string AppKey { get; set; } = "";
    public List<string> LightIds { get; set; } = [];
}

public class LightingConfigDto
{
    public string Provider { get; set; } = "tuya";
    public HueConfigDto Hue { get; set; } = new();
    public TuyaConfigDto Tuya { get; set; } = new();
    // Registered, individually addressable lights the registry section edits.
    public List<RegisteredLightEdit> Lights { get; set; } = [];
    // The state the header's "reset lights" button restores; null until configured.
    public DefaultLightEdit? DefaultLight { get; set; }
}

// Mutable form model for the default light state (mirrors the API's DefaultLightDto).
public class DefaultLightEdit
{
    public bool? Power { get; set; }
    public string? Color { get; set; }
    public int? Brightness { get; set; }
    public int? Temperature { get; set; }
}

// Mutable form model for one registered light in the Settings registry list.
public class RegisteredLightEdit
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "tuya";
    public string? HueId { get; set; }
}

public record BridgeDto(string Id, string Ip);
public record HueLightDto(string Id, string Name, string Type, bool On, bool Reachable);
public record HueRegistrationDto(string BridgeIp, string AppKey, string Hint);
public record DiscoveredTuyaDto(string Ip, string DeviceId, string ProtocolVersion, string? ProductKey);
