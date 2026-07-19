using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

/// <summary>
/// Applies what an event does to the lighting when it finishes (its <see cref="GameEvent.After"/>): restore
/// the prior lighting ("previous" — the live scene, else the configured default light — the historical
/// behavior), fully activate another scene ("scene", lights + music like tapping it) or apply the configured
/// default light ("default"). Shared by <see cref="EventActivator"/> (the legacy flash path) and
/// <c>EventTimelineRunner</c>. Best-effort: never throws, so a failing transition can't crash a trigger.
///
/// Scoped, because it depends on the per-request <see cref="ILightService"/> (via <see cref="SceneActivator"/>
/// and <see cref="SceneLightApplier"/>); the singleton timeline runner resolves it from its per-run scope.
/// </summary>
public class EventAfterApplier(
    SceneActivator sceneActivator,
    SceneLightApplier sceneLights,
    SceneStore sceneStore,
    SettingsStore settings,
    ILightService lights,
    EffectEngine effects,
    ILogger<EventAfterApplier> logger)
{
    /// <summary>True when the ending is an explicit transition (activate a scene / apply the default light)
    /// rather than "previous" — used by callers to decide whether to run it even when the event never
    /// touched the lights (a sound-only event can still end on a scene change).</summary>
    public static bool IsTransition(EventAfter? after) =>
        Eq(after?.Mode, "scene") || Eq(after?.Mode, "default");

    public async Task ApplyAsync(EventAfter? after)
    {
        try
        {
            if (Eq(after?.Mode, "scene"))
            {
                if (after!.SceneId is { } id && await sceneStore.GetAsync(id) is { } scene)
                {
                    await sceneActivator.ActivateAsync(scene);
                    return;
                }
                // Target scene is gone — fall back to restoring the prior lighting rather than doing nothing.
                logger.LogWarning("Event 'after': scene '{Id}' not found — restoring prior lighting instead.", after.SceneId);
                await sceneLights.RestoreLightsAsync();
                return;
            }

            if (Eq(after?.Mode, "default"))
            {
                if (settings.Current.DefaultLight is { } def)
                {
                    effects.StopAll();
                    await lights.ApplyAsync(def);
                }
                return;
            }

            // "previous" (or null / unknown): restore the live scene, else the default light, else leave as-is.
            await sceneLights.RestoreLightsAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Event 'after' ({Mode}) failed", after?.Mode ?? "previous");
        }
    }

    private static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
