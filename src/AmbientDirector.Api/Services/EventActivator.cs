using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

public record EventResult(string Event, string Light, string Sound)
{
    // Fail only on an actual "error: …" status (the UI's ProblemSummary keys off that prefix); "ok",
    // "skipped" and "started" (a timeline part handed to the background runner) all count as success.
    public bool FullySucceeded =>
        !Light.StartsWith("error", StringComparison.Ordinal) && !Sound.StartsWith("error", StringComparison.Ordinal);
}

/// <summary>
/// Fires a one-shot <see cref="GameEvent"/>: a brief light flash and/or overlapping sound effects, run
/// concurrently. The flash jumps the lights to the event's colour and holds for its duration; then the
/// event's ending (<see cref="GameEvent.After"/>, via <see cref="EventAfterApplier"/>) runs — by default
/// restoring the prior lighting (the live scene, else the default light), or optionally activating another
/// scene / applying the default light. Sounds overlay current playback rather than replacing it, so a
/// thunderclap lands over the tavern music.
/// </summary>
public class EventActivator(
    ILightService lights,
    EffectEngine effects,
    EventAfterApplier afterApplier,
    EventTimelineRunner runner,
    SoundStore soundStore,
    SoundFileStorage soundFiles,
    ISoundboardPlayer player,
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

        // Legacy event: flash (jump + hold) + overlapping sounds, run concurrently and awaited.
        var lightTask = RunAsync("light", () => FlashHoldAsync(evt.Flash));
        var soundTask = RunAsync("sound", () => PlaySoundsAsync(evt.SoundEffects));

        await Task.WhenAll(lightTask, soundTask);

        // Then apply what comes after the event: restore the prior lighting, activate another scene, or
        // apply the default light. "previous" only matters when the flash disturbed the lights; an explicit
        // scene/default transition runs regardless (even a sound-only event can end on a scene change).
        if (evt.Flash is not null || EventAfterApplier.IsTransition(evt.After))
            await afterApplier.ApplyAsync(evt.After);

        return new EventResult(evt.Id, lightTask.Result, soundTask.Result);
    }

    private async Task<bool> FlashHoldAsync(EventFlash? flash)
    {
        if (flash is null) return false;

        // Stop any running scene effects so they don't fight the flash, jump to the flash colour and hold;
        // the "after" step (above) hands back to whatever should be showing once the sounds have fired too.
        effects.StopAll();
        await lights.SetColorAsync(flash.Color, flash.Brightness);
        await Task.Delay(flash.DurationMs);
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
            // Fold into a stable, displayable code; the panel renders it in its language (see UiExtensions).
            return "error:" + ErrorClassifier.DisplayCodeFor(ex);
        }
    }
}
