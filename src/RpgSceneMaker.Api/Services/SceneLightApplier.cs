using System.Collections.Concurrent;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Applies a scene's lighting only — per-light entries (each optionally animated by a background effect)
/// or the legacy "all lights" block — and returns an "ok"/"skipped"/"error: …" status string. Shared by
/// <see cref="SceneActivator"/> (full scene activation) and <see cref="EventActivator"/> (restoring the
/// lights after an event's flash). Never throws: light failures are caught and folded into the status.
/// </summary>
public class SceneLightApplier(
    ILightService lights,
    LightRegistry registry,
    EffectEngine effects,
    SceneStore sceneStore,
    SettingsStore settings,
    CurrentState state,
    ILogger<SceneLightApplier> logger)
{
    // Per-light mode wins; legacy Light is the "all lights" fallback; a scene may also not touch lights at all.
    public async Task<string> ApplyAsync(Scene scene)
    {
        if (scene.Lights.Count > 0)
        {
            effects.StopAll();
            var errors = new ConcurrentBag<(string Key, string Message)>();
            var jobs = new ConcurrentBag<EffectJob>();

            await Task.WhenAll(scene.Lights.Select(async entry =>
            {
                try
                {
                    var resolved = registry.Resolve(entry.LightKey);
                    await ApplyBaseAsync(resolved.Service, resolved.TargetId, entry);
                    if (entry.Effect is not null)
                        jobs.Add(new EffectJob(resolved.Service, resolved.TargetId, resolved.IsHue, entry));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Scene activation: light '{Key}' failed", entry.LightKey);
                    errors.Add((entry.LightKey, ex.Message));
                }
            }));

            if (!jobs.IsEmpty)
                await effects.StartAsync([.. jobs]);

            return errors.IsEmpty
                ? "ok"
                : "error: " + string.Join("; ", errors.Select(e => $"{e.Key}: {e.Message}"));
        }

        if (scene.Light is not null)
        {
            effects.StopAll();
            try
            {
                await lights.ApplyAsync(scene.Light);
                return "ok";
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scene activation: light failed");
                return $"error: {ex.Message}";
            }
        }

        // Scene doesn't touch lights — leave any running effect alone.
        return "skipped";
    }

    // Apply the static base state so the light reaches a sensible look before the effect loop's first tick.
    // Power-off wins, else colour, else white, else power-on. Shared with EventTimelineRunner's clips.
    internal static async Task ApplyBaseAsync(ILightService service, string? targetId, SceneLight e)
    {
        if (e.Power == false)
        {
            await service.SetPowerAsync(false, targetId);
            return;
        }
        if (!string.IsNullOrWhiteSpace(e.Color))
            await service.SetColorAsync(e.Color, e.Brightness, targetId);
        else if (e.Brightness is not null || e.Temperature is not null)
            await service.SetWhiteAsync(e.Brightness ?? 100, e.Temperature, targetId);
        else if (e.Power == true)
            await service.SetPowerAsync(true, targetId);
    }

    /// <summary>Restore the lighting after an event's flash/timeline: the live scene if one is active
    /// (re-running its effects), else the configured default light, else leave the lights as-is (there's
    /// nothing known to restore to). Shared by <see cref="EventActivator"/> and <c>EventTimelineRunner</c>.</summary>
    public async Task RestoreLightsAsync()
    {
        if (state.ActiveSceneId is { } id && await sceneStore.GetAsync(id) is { } scene)
        {
            await ApplyAsync(scene);
            return;
        }

        if (settings.Current.DefaultLight is { } def)
        {
            effects.StopAll();
            await lights.ApplyAsync(def);
        }
    }
}
