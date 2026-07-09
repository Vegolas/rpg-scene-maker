using System.Collections.Concurrent;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

public record ActivationResult(string Scene, string Light, string Music, string SoundEffects)
{
    public bool FullySucceeded => Light is "ok" or "skipped" && Music is "ok" or "skipped" && SoundEffects is "ok" or "skipped";
}

/// <summary>Applies a scene: light, music and sound effects run concurrently so the table switches fast.</summary>
public class SceneActivator(
    ILightService lights,
    LightRegistry registry,
    EffectEngine effects,
    KenkuClient kenku,
    SpotifyClient spotify,
    SpotifyStore spotifyStore,
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
            // With Spotify connected, Kenku may legitimately be off — don't let it fail the scene.
            if (spotifyStore.Current.IsConnected)
            {
                await BestEffort(kenku.PauseAsync);
                await spotify.PauseAsync(throwOnNoDevice: false);
            }
            else
            {
                await kenku.PauseAsync();
            }
            return true;
        }

        // Spotify track/playlist/album/artist reference → play on Spotify, best-effort pause Kenku.
        if (!string.IsNullOrWhiteSpace(music.PlayId) && SpotifyClient.IsSpotifyUri(music.PlayId))
        {
            // Play first: it wakes the preferred device, while volume on an idle device can 404.
            await spotify.PlayAsync(music.PlayId);
            if (music.Volume is double spotifyVolume)
                await spotify.SetVolumeAsync(spotifyVolume);
            await BestEffort(kenku.PauseAsync);
            return true;
        }

        // Kenku (default): set volume / play, and best-effort pause Spotify if it's connected.
        var didSomething = false;
        if (music.Volume is double volume)
        {
            await kenku.SetVolumeAsync(volume);
            didSomething = true;
        }
        if (!string.IsNullOrWhiteSpace(music.PlayId))
        {
            await kenku.PlayAsync(music.PlayId);
            await BestEffortPauseSpotifyAsync();
            didSomething = true;
        }
        return didSomething;
    }

    /// <summary>Pause Spotify without failing the scene: only when connected, and never throwing.</summary>
    private async Task BestEffortPauseSpotifyAsync()
    {
        if (!spotifyStore.Current.IsConnected) return;
        await BestEffort(() => spotify.PauseAsync(throwOnNoDevice: false));
    }

    private async Task BestEffort(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { logger.LogDebug(ex, "Best-effort music cross-pause failed (ignored)"); }
    }

    private async Task<bool> ApplySoundEffectsAsync(List<string> soundIds)
    {
        if (soundIds.Count == 0) return false;
        foreach (var id in soundIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            await kenku.PlaySoundAsync(id);
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
