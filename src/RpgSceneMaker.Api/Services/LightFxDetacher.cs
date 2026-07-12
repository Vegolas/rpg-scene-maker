using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Detaches every live reference to a Light FX before it is deleted: each scene-light / event-timeline light
/// clip whose effect points at the FX (type "fx") is rewritten in place to embed a "custom" copy of the FX's
/// keyframes, so nothing is left dangling and behaviour is unchanged. Shared by the /lightfx delete endpoint
/// and the AI tool façade so both delete the same way. Mirrors how deleting a sound scrubs its id.
/// </summary>
public static class LightFxDetacher
{
    // Replace each scene-light / timeline-light effect that references the given FX with an embedded "custom"
    // copy, saving only the scenes/events actually touched.
    public static async Task DetachReferencesAsync(LightFx effect, SceneStore scenes, EventStore events)
    {
        foreach (var scene in await scenes.GetAllAsync())
        {
            var dirty = false;
            foreach (var light in scene.Lights)
                if (IsReference(light.Effect, effect.Id)) { light.Effect = SceneLightApplier.MaterializeFx(effect); dirty = true; }
            if (dirty)
                await scenes.UpsertAsync(scene);
        }

        foreach (var evt in await events.GetAllAsync())
        {
            if (evt.Timeline is not { } timeline) continue;
            var dirty = false;
            foreach (var clip in timeline.Lights)
                if (IsReference(clip.Effect, effect.Id)) { clip.Effect = SceneLightApplier.MaterializeFx(effect); dirty = true; }
            if (dirty)
                await events.UpsertAsync(evt);
        }
    }

    private static bool IsReference(LightEffect? effect, string fxId) =>
        effect is { Type: "fx" } && string.Equals(effect.FxId, fxId, StringComparison.OrdinalIgnoreCase);
}
