using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

public record ActivationResult(string Scene, string Light, string Music, string SoundEffects)
{
    public bool FullySucceeded =>
        Light is "ok" or "skipped" && Music is "ok" or "skipped" && SoundEffects is "ok" or "skipped";
}

/// <summary>Applies a scene: light, music and sound effects run concurrently so the table switches fast.</summary>
public class SceneActivator(
    SceneLightApplier sceneLights,
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

        var lightTask = sceneLights.ApplyAsync(scene);
        var musicTask = RunAsync("music", () => ApplyMusicAsync(scene.Music));
        var sfxTask = RunAsync("sfx", () => ApplySoundEffectsAsync(scene.SoundEffects));

        await Task.WhenAll(lightTask, musicTask, sfxTask);
        return new ActivationResult(scene.Id, lightTask.Result, musicTask.Result, sfxTask.Result);
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
                throw new ValidationException("error.music.notSpotifyUri", music.PlayId);

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
            // Fold into a stable, displayable code; the panel renders it in its language (see UiExtensions).
            return "error:" + ErrorClassifier.DisplayCodeFor(ex);
        }
    }
}
