using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Errors;

namespace RpgSceneMaker.Api.Services;

/// <summary>A registered light resolved to the concrete service that drives it.</summary>
public record ResolvedLight(ILightService Service, string? TargetId, bool IsHue);

/// <summary>
/// Maps the configured light registry (SettingsStore) to concrete light services.
/// Tuya entries resolve to the shared TuyaLightService (single bulb, no target); hue entries
/// resolve to HueLightService addressing the entry's HueId.
/// </summary>
public class LightRegistry(IServiceProvider services, SettingsStore settings)
{
    public IReadOnlyList<RegisteredLight> GetAll() => settings.Current.Lights;

    public ResolvedLight Resolve(string key)
    {
        var light = settings.Current.Lights.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException("error.light.unknown", key);

        return light.Provider.Equals("hue", StringComparison.OrdinalIgnoreCase)
            ? new ResolvedLight(services.GetRequiredService<HueLightService>(), light.HueId, IsHue: true)
            : new ResolvedLight(services.GetRequiredService<TuyaLightService>(), null, IsHue: false);
    }
}
