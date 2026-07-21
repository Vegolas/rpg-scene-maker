using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services.Music;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Services.Sharing;

/// <summary>A scene: bundles its referenced Light FX and sounds, carries its tile art, and exposes its per-light
/// bulb bindings for the import remap step.</summary>
public sealed class SceneShareDescriptor(SceneStore store) : ShareDescriptor<Scene>
{
    public override string Kind => "scene";
    protected override Task<Scene?> GetAsync(string id) => store.GetAsync(id);
    protected override Task UpsertAsync(Scene scene) => store.UpsertAsync(scene);
    protected override void Validate(Scene scene) => SceneValidation.Validate(scene);
    protected override string GetId(Scene scene) => scene.Id;
    protected override void SetId(Scene scene, string id) => scene.Id = id;
    protected override string GetName(Scene scene) => scene.Name;

    protected override IEnumerable<MediaRef> Media(Scene scene)
    {
        if (!string.IsNullOrEmpty(scene.Image)) yield return new(MediaKind.Image, scene.Image!);
    }

    protected override IEnumerable<DepRef> Dependencies(Scene scene)
    {
        foreach (var light in scene.Lights ?? [])
            if (light.Effect is { Type: "fx", FxId: { } fxId } && !string.IsNullOrEmpty(fxId))
                yield return new("lightfx", fxId);
        foreach (var soundId in scene.SoundEffects ?? [])
            if (!string.IsNullOrEmpty(soundId))
                yield return new("sound", soundId);
        // Music.PlayId (Spotify URI / local:track ref) is portable text — no bundled dependency.
    }

    protected override IEnumerable<LightKeySite> LightKeys(Scene scene)
    {
        foreach (var light in scene.Lights ?? [])
            if (!string.IsNullOrEmpty(light.LightKey))
                yield return new(light.LightKey, $"Scene '{scene.Name}'");
    }

    protected override string? Sanitize(Scene scene)
    {
        // The one field a pack can carry in an unusable state (e.g. the starter template's
        // "PASTE-SPOTIFY-URI-…" placeholder): a music id that is neither a Spotify nor a local reference. Null
        // it so the scene imports; the GM re-sets it in the editor. Every other field was validated at author
        // time on the source. Mirrors the check in SceneValidation (error.scene.musicUri).
        if (scene.Music is { PlayId: { } playId } && !string.IsNullOrWhiteSpace(playId)
            && !SpotifyClient.IsSpotifyUri(playId) && !LocalMusicId.IsLocal(playId))
        {
            scene.Music.PlayId = null;
            return "share.repaired.music";
        }
        return null;
    }

    protected override void Rewrite(Scene scene, ShareRewriteContext ctx)
    {
        scene.Image = ctx.MapMedia(scene.Image);

        scene.SoundEffects = (scene.SoundEffects ?? [])
            .Select(id => ctx.MapDep("sound", id))
            .OfType<string>()                       // drop sounds that weren't in the pack
            .ToList();

        var kept = new List<SceneLight>();
        var takenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var light in scene.Lights ?? [])
        {
            var target = ctx.MapLightKey(light.LightKey);
            if (target is null) continue;            // skipped / unmapped → drop the entry (a slug is required)
            if (!takenTargets.Add(target)) continue; // two source keys → one bulb: keep the first only
            light.LightKey = target;
            if (light.Effect is { Type: "fx", FxId: { } fxId })
            {
                var newFx = ctx.MapDep("lightfx", fxId);
                if (newFx is null) light.Effect = null;   // dangling FX → static light (never bind a same-named local FX)
                else light.Effect.FxId = newFx;
            }
            kept.Add(light);
        }
        scene.Lights = kept;
    }
}
