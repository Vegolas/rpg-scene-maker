using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

public record EventResult(string Event, string Light, string Sound)
{
    // Fail only on an actual "error: …" status (the UI's ProblemSummary keys off that prefix); "ok",
    // "skipped" and "started" (a timeline part handed to the background runner) all count as success.
    public bool FullySucceeded =>
        !Light.StartsWith("error", StringComparison.Ordinal) && !Sound.StartsWith("error", StringComparison.Ordinal);
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
    EventTimelineRunner runner,
    SoundStore soundStore,
    SoundFileStorage soundFiles,
    SoundboardPlayer player,
    ILogger<EventActivator> logger)
{
    public async Task<EventResult> TriggerAsync(GameEvent evt)
    {
        // A non-empty timeline runs in the background: hand it to the singleton runner and return at once.
        // Each part reports "started" when it has clips, else "skipped" (validation keeps a timeline event
        // free of legacy flash/sounds, so this fully describes the trigger).
        if (evt.Timeline is { } timeline && (timeline.Sounds.Count > 0 || timeline.Lights.Count > 0))
        {
            runner.Start(evt);
            return new EventResult(evt.Id,
                timeline.Lights.Count > 0 ? "started" : "skipped",
                timeline.Sounds.Count > 0 ? "started" : "skipped");
        }

        // Legacy event: flash + overlapping sounds, run concurrently and awaited.
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
        await sceneLights.RestoreLightsAsync();
        return true;
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
