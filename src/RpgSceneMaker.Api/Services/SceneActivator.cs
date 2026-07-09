using System.Collections.Concurrent;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

public record ActivationResult(string Scene, string Light, string Music, string SoundEffects)
{
    public bool FullySucceeded =>
        Light is "ok" or "skipped" && Music is "ok" or "skipped" && SoundEffects is "ok" or "skipped";
}

/// <summary>Applies a scene: light, music and sound effects run concurrently so the table switches fast.</summary>
public class SceneActivator(
    ILightService lights,
    LightRegistry registry,
    EffectEngine effects,
    SpotifyClient spotify,
    SoundStore soundStore,
    SoundFileStorage soundFiles,
    SoundboardPlayer player,
    CurrentState state,
    ILogger<SceneActivator> logger)
{
    public async Task<ActivationResult> ActivateAsync(Scene scene)
    {
        state.ActiveSceneId = scene.Id;
        state.ActivatedAt = DateTimeOffset.Now;

        var lightTask = ApplyLightsAsync(scene);
        var musicTask = RunAsync("music", () => ApplyMusicAsync(scene.Music));
        var sfxTask = RunAsync("sfx", () => ApplySoundEffectsAsync(scene.SoundEffects));

        await Task.WhenAll(lightTask, musicTask, sfxTask);
        return new ActivationResult(scene.Id, lightTask.Result, musicTask.Result, sfxTask.Result);
    }

    // Per-light mode wins; legacy Light is the "all lights" fallback; a scene may also not touch lights at all.
    private async Task<string> ApplyLightsAsync(Scene scene)
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
                    await ApplyBaseAsync(resolved, entry);
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
    private static async Task ApplyBaseAsync(ResolvedLight r, SceneLight e)
    {
        if (e.Power == false)
        {
            await r.Service.SetPowerAsync(false, r.TargetId);
            return;
        }
        if (!string.IsNullOrWhiteSpace(e.Color))
            await r.Service.SetColorAsync(e.Color, e.Brightness, r.TargetId);
        else if (e.Brightness is not null || e.Temperature is not null)
            await r.Service.SetWhiteAsync(e.Brightness ?? 100, e.Temperature, r.TargetId);
        else if (e.Power == true)
            await r.Service.SetPowerAsync(true, r.TargetId);
    }

    private async Task<bool> ApplyMusicAsync(MusicSettings? music)
    {
        if (music is null) return false;

        if (music.Pause)
        {
            // Best-effort pause so a scene without a live device still succeeds.
            await spotify.PauseAsync(throwOnNoDevice: false);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(music.PlayId))
        {
            if (!SpotifyClient.IsSpotifyUri(music.PlayId))
                throw new ArgumentException(
                    $"Music id is not a Spotify link/URI: '{music.PlayId}' — Kenku support was removed; use a spotify: URI or open.spotify.com link.");

            // Play first: it wakes the preferred device, while volume on an idle device can 404.
            await spotify.PlayAsync(music.PlayId);
            if (music.Volume is double playVolume)
                await spotify.SetVolumeAsync(playVolume);
            return true;
        }

        // Volume-only tweak with no track to start.
        if (music.Volume is double volume)
        {
            await spotify.SetVolumeAsync(volume);
            return true;
        }

        return false;
    }

    // A scene that carries sound effects swaps them in cleanly: stop whatever's playing, then fire its
    // own. An empty list leaves playback untouched (reported "skipped"), like music with nothing to change.
    private async Task<bool> ApplySoundEffectsAsync(List<string> soundEffects)
    {
        var ids = soundEffects.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (ids.Count == 0) return false;

        player.StopAll();
        var missing = new List<string>();
        foreach (var id in ids)
        {
            if (await soundStore.GetAsync(id) is { } sound)
                player.Play(sound.Id, soundFiles.FullPath(sound), sound.Loop, sound.Volume);
            else
                missing.Add(id);
        }

        if (missing.Count > 0)
            logger.LogWarning("Scene activation: sound(s) not found and skipped: {Missing}", string.Join(", ", missing));
        return true;
    }

    private async Task<string> RunAsync(string part, Func<Task<bool>> action)
    {
        try
        {
            return await action() ? "ok" : "skipped";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scene activation: {Part} failed", part);
            return $"error: {ex.Message}";
        }
    }
}
