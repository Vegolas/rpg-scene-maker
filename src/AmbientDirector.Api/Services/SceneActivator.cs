using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services.Music;

namespace AmbientDirector.Api.Services;

public record ActivationResult(string Scene, string Light, string Music, string SoundEffects)
{
    public bool FullySucceeded =>
        Light is "ok" or "skipped" && Music is "ok" or "skipped" && SoundEffects is "ok" or "skipped";
}

/// <summary>Applies a scene: light, music and sound effects run concurrently so the table switches fast.</summary>
public class SceneActivator(
    SceneLightApplier sceneLights,
    MusicRouter router,
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

        var lightTask = sceneLights.ApplyAsync(scene);
        var musicTask = RunAsync("music", () => ApplyMusicAsync(scene.Music));
        var sfxTask = RunAsync("sfx", () => ApplySoundEffectsAsync(scene.SoundEffects));

        await Task.WhenAll(lightTask, musicTask, sfxTask);
        return new ActivationResult(scene.Id, lightTask.Result, musicTask.Result, sfxTask.Result);
    }

    /// <summary>Stops the live scene: return the lights to their configured default, pause music and stop
    /// every sound, and clear the "currently showing" highlight. Each part runs concurrently and reports
    /// ok/skipped/error independently (like activation), so one failing part doesn't block the others.
    /// Music and lights are best-effort — an unconnected Spotify or a scene left with no default lighting
    /// is reported "skipped", not an error.</summary>
    public async Task<ActivationResult> StopAsync()
    {
        var stopped = state.ActiveSceneId ?? "";
        state.ActiveSceneId = null;
        state.ActivatedAt = null;

        var lightTask = RunAsync("light", () => sceneLights.ResetToDefaultAsync());
        var musicTask = RunAsync("music", PauseMusicAsync);
        var sfxTask = RunAsync("sfx", StopSoundsAsync);

        await Task.WhenAll(lightTask, musicTask, sfxTask);
        return new ActivationResult(stopped, lightTask.Result, musicTask.Result, sfxTask.Result);
    }

    // Pause the music a scene was playing (the active source). Silent no-op when nothing is playing (no active
    // source and Spotify not connected → reported "skipped"), and a missing device is tolerated
    // (throwOnNoDevice: false), so stopping never fails just because of the music.
    private async Task<bool> PauseMusicAsync()
    {
        if (router.ResolveActiveOrNull() is not { } source) return false;
        await source.PauseAsync(throwOnNoDevice: false);
        return true;
    }

    private Task<bool> StopSoundsAsync()
    {
        player.StopAll();
        return Task.FromResult(true);
    }

    private async Task<bool> ApplyMusicAsync(MusicSettings? music)
    {
        if (music is null) return false;

        if (music.Pause)
        {
            // Best-effort pause of the targeted (or active) source so a scene without a live device still succeeds.
            await router.Resolve(music.Source).PauseAsync(throwOnNoDevice: false);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(music.PlayId))
        {
            // Route by the scene's explicit Source, else infer from the PlayId shape. Play first (it wakes a
            // Spotify device, and volume on an idle device can 404), then set volume on the same source.
            var key = await router.PlayAsync(music.PlayId, music.Source);
            if (music.Volume is double playVolume)
                await router.Resolve(key).SetVolumeAsync(playVolume);
            return true;
        }

        // Volume-only tweak with no track to start.
        if (music.Volume is double volume)
        {
            await router.Resolve(music.Source).SetVolumeAsync(volume);
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
            // Fold into a stable, displayable code; the panel renders it in its language (see UiExtensions).
            return "error:" + ErrorClassifier.DisplayCodeFor(ex);
        }
    }
}
