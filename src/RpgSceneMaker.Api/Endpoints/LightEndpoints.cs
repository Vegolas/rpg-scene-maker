using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Endpoints;

public static class LightEndpoints
{
    public static void MapLightEndpoints(this WebApplication app)
    {
        // Manual light control (and scene activation) always ends any running effect loops.
        var lights = app.MapGroup("/lights");

        lights.MapMethods("/on", EndpointHelpers.GetOrPost, async (ILightService bulb, EffectEngine effects) =>
        {
            effects.StopAll();
            await bulb.SetPowerAsync(true);
            return new { light = "on" };
        });

        lights.MapMethods("/off", EndpointHelpers.GetOrPost, async (ILightService bulb, EffectEngine effects) =>
        {
            effects.StopAll();
            await bulb.SetPowerAsync(false);
            return new { light = "off" };
        });

        lights.MapMethods("/toggle", EndpointHelpers.GetOrPost, async (ILightService bulb, EffectEngine effects) =>
        {
            effects.StopAll();
            return new { light = await bulb.ToggleAsync() ? "on" : "off" };
        });

        // /lights/color?hex=FF8C2A&brightness=80
        lights.MapMethods("/color", EndpointHelpers.GetOrPost, async (string hex, int? brightness, ILightService bulb, EffectEngine effects) =>
        {
            effects.StopAll();
            await bulb.SetColorAsync(hex, brightness);
            return new { light = "colour", hex, brightness };
        });

        // /lights/white?brightness=80&temperature=30   (temperature: 0 warm - 100 cold)
        lights.MapMethods("/white", EndpointHelpers.GetOrPost, async (int? brightness, int? temperature, ILightService bulb, EffectEngine effects) =>
        {
            effects.StopAll();
            await bulb.SetWhiteAsync(brightness ?? 100, temperature);
            return new { light = "white", brightness = brightness ?? 100, temperature };
        });

        // /lights/brightness?value=50
        lights.MapMethods("/brightness", EndpointHelpers.GetOrPost, async (int value, ILightService bulb, EffectEngine effects) =>
        {
            effects.StopAll();
            await bulb.SetBrightnessAsync(value);
            return new { brightness = value };
        });

        lights.MapGet("/status", (ILightService bulb) => bulb.GetStatusAsync());

        // Registered lights the per-light endpoints and scenes can target.
        lights.MapGet("/list", (LightRegistry registry) =>
            registry.GetAll().Select(l => new { key = l.Key, name = l.Name, provider = l.Provider }));

        // ---- Per-light manual control (by registry key) ----
        lights.MapMethods("/{key}/on", EndpointHelpers.GetOrPost, async (string key, LightRegistry registry, EffectEngine effects) =>
        {
            effects.StopAll();
            var r = registry.Resolve(key);
            await r.Service.SetPowerAsync(true, r.TargetId);
            return new { key, light = "on" };
        });

        lights.MapMethods("/{key}/off", EndpointHelpers.GetOrPost, async (string key, LightRegistry registry, EffectEngine effects) =>
        {
            effects.StopAll();
            var r = registry.Resolve(key);
            await r.Service.SetPowerAsync(false, r.TargetId);
            return new { key, light = "off" };
        });

        // /lights/{key}/color?hex=FF8C2A&brightness=80
        lights.MapMethods("/{key}/color", EndpointHelpers.GetOrPost, async (string key, string hex, int? brightness, LightRegistry registry, EffectEngine effects) =>
        {
            effects.StopAll();
            var r = registry.Resolve(key);
            await r.Service.SetColorAsync(hex, brightness, r.TargetId);
            return new { key, light = "colour", hex, brightness };
        });

        // /lights/{key}/white?brightness=80&temperature=30
        lights.MapMethods("/{key}/white", EndpointHelpers.GetOrPost, async (string key, int? brightness, int? temperature, LightRegistry registry, EffectEngine effects) =>
        {
            effects.StopAll();
            var r = registry.Resolve(key);
            await r.Service.SetWhiteAsync(brightness ?? 100, temperature, r.TargetId);
            return new { key, light = "white", brightness = brightness ?? 100, temperature };
        });

        // /lights/{key}/brightness?value=50
        lights.MapMethods("/{key}/brightness", EndpointHelpers.GetOrPost, async (string key, int value, LightRegistry registry, EffectEngine effects) =>
        {
            effects.StopAll();
            var r = registry.Resolve(key);
            await r.Service.SetBrightnessAsync(value, r.TargetId);
            return new { key, brightness = value };
        });
    }
}
