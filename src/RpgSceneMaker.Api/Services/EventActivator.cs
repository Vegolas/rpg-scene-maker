using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

public record EventResult(string Event, string Light, string Sound)
{
    public bool FullySucceeded => Light is "ok" or "skipped" && Sound is "ok" or "skipped";
}

/// <summary>
/// Fires a one-shot <see cref="GameEvent"/>: a brief light flash and/or overlapping sound effects, run
/// concurrently. The flash jumps the lights to the event's colour, holds for its duration, then restores
/// the prior lighting — the live scene if one is active (re-running its effects), else the configured
/// default light, else it leaves the flash as-is (nothing known to restore to). Sounds overlay current
/// playback rather than replacing it, so a thunderclap lands over the tavern music.
/// </summary>
public class EventActivator(
    ILightService lights,
    EffectEngine effects,
    SceneLightApplier sceneLights,
    SceneStore sceneStore,
    SettingsStore settings,
    CurrentState state,
    SoundStore soundStore,
    SoundFileStorage soundFiles,
    SoundboardPlayer player,
    ILogger<EventActivator> logger)
{
    public async Task<EventResult> TriggerAsync(GameEvent evt)
    {
        var lightTask = RunAsync("light", () => FlashAsync(evt.Flash));
        var soundTask = RunAsync("sound", () => PlaySoundsAsync(evt.SoundEffects));

        await Task.WhenAll(lightTask, soundTask);
        return new EventResult(evt.Id, lightTask.Result, soundTask.Result);
    }

    private async Task<bool> FlashAsync(EventFlash? flash)
    {
        if (flash is null) return false;

        // Stop any running scene effects so they don't fight the flash, jump to the flash colour,
        // hold, then hand back to whatever should be showing.
        effects.StopAll();
        await lights.SetColorAsync(flash.Color, flash.Brightness);
        await Task.Delay(flash.DurationMs);
        await RestoreLightsAsync();
        return true;
    }

    // Return to the live scene's lighting (this also restarts its effects); otherwise the configured
    // default light; otherwise leave the flash showing — there's nothing known to restore to.
    private async Task RestoreLightsAsync()
    {
        if (state.ActiveSceneId is { } id && await sceneStore.GetAsync(id) is { } scene)
        {
            await sceneLights.ApplyAsync(scene);
            return;
        }

        if (settings.Current.DefaultLight is { } def)
        {
            effects.StopAll();
            await lights.ApplyAsync(def);
        }
    }

    private async Task<bool> PlaySoundsAsync(List<string> soundEffects)
    {
        var ids = soundEffects.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (ids.Count == 0) return false;

        // Overlay, don't replace: no StopAll here, unlike a scene's sound effects.
        var missing = new List<string>();
        foreach (var id in ids)
        {
            if (await soundStore.GetAsync(id) is { } sound)
                player.Play(sound.Id, soundFiles.FullPath(sound), sound.Loop, sound.Volume);
            else
                missing.Add(id);
        }

        if (missing.Count > 0)
            logger.LogWarning("Event trigger: sound(s) not found and skipped: {Missing}", string.Join(", ", missing));
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
            logger.LogWarning(ex, "Event trigger: {Part} failed", part);
            return $"error: {ex.Message}";
        }
    }
}
