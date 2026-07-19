using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Data;

/// <summary>
/// The lighting configuration, stored as a single row (Id = 1) in SQLite.
/// Edited from the Settings page / PUT /setup/config; the database is the source of truth.
/// </summary>
public class LightingConfig
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Which light system scenes and /lights endpoints control: "tuya" or "hue".</summary>
    public string Provider { get; set; } = "tuya";

    public HueConfig Hue { get; set; } = new();

    public TuyaConfig Tuya { get; set; } = new();

    /// <summary>Individually addressable lights that scenes and /lights/{key} endpoints control.</summary>
    public List<RegisteredLight> Lights { get; set; } = [];

    /// <summary>
    /// The "default state" the panel's always-visible reset button (GET/POST /lights/default) restores —
    /// e.g. bright white "house lights up" for a break. Null = not configured (the reset button then 400s).
    /// </summary>
    public LightSettings? DefaultLight { get; set; }

    /// <summary>
    /// When the guided first-run onboarding wizard was completed (or auto-completed for an existing install).
    /// Null = never done, so the panel shows the wizard on first load. Lives on this single-row config as the
    /// natural home for a one-per-install flag (see GET /setup/onboarding).
    /// </summary>
    public DateTimeOffset? OnboardingDoneUtc { get; set; }
}

/// <summary>One addressable light in the registry. Key is a stable slug used by scenes and endpoints.</summary>
public class RegisteredLight
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>"tuya" or "hue".</summary>
    public string Provider { get; set; } = "tuya";

    /// <summary>Hue Bridge light id; required when Provider == "hue", ignored for tuya (the single bulb).</summary>
    public string? HueId { get; set; }
}

public class HueConfig
{
    /// <summary>Hue Bridge IP (find it with GET /setup/hue/discover).</summary>
    public string BridgeIp { get; set; } = "";

    /// <summary>App key / username (create it with GET /setup/hue/register?bridgeIp=...).</summary>
    public string AppKey { get; set; } = "";

    /// <summary>
    /// Hue light ids to control (see GET /setup/hue/lights).
    /// Leave empty to control every light on the bridge.
    /// </summary>
    public List<string> LightIds { get; set; } = [];
}

public class TuyaConfig
{
    /// <summary>Local IP address of the bulb (find it with GET /setup/scan).</summary>
    public string Ip { get; set; } = "";

    /// <summary>Tuya device id (from the Smart Life app or GET /setup/local-keys).</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>Local encryption key (from GET /setup/local-keys).</summary>
    public string LocalKey { get; set; } = "";

    /// <summary>"3.3" (most bulbs) or "3.1" (very old firmware).</summary>
    public string ProtocolVersion { get; set; } = "3.3";

    /// <summary>
    /// Data-point layout of the bulb.
    /// "v2" = DPs 20-24 (most bulbs, brightness 10-1000),
    /// "v1" = DPs 1-5 (older bulbs, brightness 25-255).
    /// Check with GET /lights/status: if you see keys 20/21/22 use v2, keys 1/2/3 use v1.
    /// </summary>
    public string DpProfile { get; set; } = "v2";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Ip) &&
        !string.IsNullOrWhiteSpace(DeviceId) &&
        !string.IsNullOrWhiteSpace(LocalKey);
}
