using System.Collections.Concurrent;
using RpgSceneMaker.Api.Errors;
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
    LightFxStore fxStore,
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

            // Resolve any "fx" effects once up front (not per engine tick) so the job carries a materialized
            // "custom" effect and the EffectEngine stays untouched.
            var fxLib = await LoadFxLibraryAsync(scene.Lights.Select(l => l.Effect));

            await Task.WhenAll(scene.Lights.Select(async entry =>
            {
                try
                {
                    var resolved = registry.Resolve(entry.LightKey);
                    await ApplyBaseAsync(resolved.Service, resolved.TargetId, entry);
                    if (ResolveEffect(entry, fxLib, $"light '{entry.LightKey}'") is { } jobLight)
                        jobs.Add(new EffectJob(resolved.Service, resolved.TargetId, resolved.IsHue, jobLight));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Scene activation: light '{Key}' failed", entry.LightKey);
                    errors.Add((entry.LightKey, ex.Message));
                }
            }));

            if (!jobs.IsEmpty)
                await effects.StartAsync([.. jobs]);

            // A single displayable code for the toast — the per-light detail is in the warnings logged above.
            return errors.IsEmpty ? "ok" : "error:error.activation.lights";
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
                return "error:" + ErrorClassifier.DisplayCodeFor(ex);
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

    // Preload the FX library into a case-insensitive dictionary when any effect references it ("fx"), so the
    // per-light resolution below doesn't open a DbContext per entry. Null when no effect references the library.
    private async Task<Dictionary<string, LightFx>?> LoadFxLibraryAsync(IEnumerable<LightEffect?> effects)
    {
        if (!effects.Any(e => e is { Type: "fx" })) return null;
        return (await fxStore.GetAllAsync()).ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
    }

    // The light (with its effect) to run as a background job for this entry, or null when there's no effect —
    // or the effect is a dangling "fx" reference (logged; the static base already applied stands in). For a
    // resolved "fx" the entry's base state is kept and the effect swapped for the library FX's materialized
    // "custom" effect; every other effect type runs the entry as-is.
    private SceneLight? ResolveEffect(SceneLight entry, IReadOnlyDictionary<string, LightFx>? fxLib, string context)
    {
        if (entry.Effect is not { } fx) return null;
        if (fx.Type != "fx") return entry;

        if (fxLib is null || fx.FxId is null || !fxLib.TryGetValue(fx.FxId, out var lib))
        {
            logger.LogWarning("{Context}: Light FX '{FxId}' not found — showing a static light", context, fx.FxId);
            return null;
        }
        return WithEffect(entry, MaterializeFx(lib));
    }

    /// <summary>Materialize a library <see cref="LightFx"/> into the equivalent in-memory "custom" effect the
    /// <see cref="EffectEngine"/> runs. Shared with <see cref="EventTimelineRunner"/>.</summary>
    internal static LightEffect MaterializeFx(LightFx fx) =>
        new() { Type = "custom", Keyframes = fx.Keyframes, Loop = fx.Loop, CycleMs = fx.CycleMs };

    // A shallow copy of the scene light with its effect replaced (base colour/brightness/etc. preserved).
    private static SceneLight WithEffect(SceneLight src, LightEffect effect) => new()
    {
        LightKey = src.LightKey,
        Power = src.Power,
        Color = src.Color,
        Brightness = src.Brightness,
        Temperature = src.Temperature,
        Effect = effect,
    };

    /// <summary>Return the lights to the configured default state — used when <b>stopping</b> a scene.
    /// When no default is configured there's no defined neutral to fall back to, so the lights are left
    /// as-is and this returns <c>false</c> ("skipped"); unlike the reset-lights button
    /// (<c>/lights/default</c>), which surfaces a missing default as an error. Returns whether a default
    /// was applied.</summary>
    public async Task<bool> ResetToDefaultAsync()
    {
        if (settings.Current.DefaultLight is not { } def) return false;
        effects.StopAll();
        await lights.ApplyAsync(def);
        return true;
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
