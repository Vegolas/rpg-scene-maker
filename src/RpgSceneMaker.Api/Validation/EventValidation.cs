using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Guards an event coming from the editor before it reaches the store; failures map to HTTP 400.</summary>
public static class EventValidation
{
    // A flash held longer than this stops feeling like a one-shot; also bounds the light-restore delay.
    private const int MaxFlashDurationMs = 10_000;

    // A single timeline clip must last at least this long (below it the light/sound can't keep up) …
    private const int MinClipDurationMs = 100;
    // … and no clip — nor the whole timeline — may run past this (bounds the background runner).
    private const int MaxTimelineMs = 600_000;

    // Ids that would shadow the literal /events/{list,stop,state} routes if used at GET /events/{id}.
    private static readonly string[] ReservedIds = ["list", "stop", "state"];

    public static void Validate(GameEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Id))
            throw new ValidationException("error.common.idRequired");
        if (!LightValidation.IsSlug(evt.Id))
            throw new ValidationException("error.common.idSlug");
        if (ReservedIds.Contains(evt.Id, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("error.event.reservedId");
        if (string.IsNullOrWhiteSpace(evt.Name))
            throw new ValidationException("error.common.nameRequired");
        if (evt.Image is not null && !ImageFileStorage.IsValidName(evt.Image))
            throw new ValidationException("error.common.invalidImage");

        if (evt.Flash is { } flash)
        {
            flash.Color = LightValidation.NormalizeHex(flash.Color);
            if (flash.Brightness is < 0 or > 100)
                throw new ValidationException("error.event.flashBrightnessRange");
            if (flash.DurationMs is < 1 or > MaxFlashDurationMs)
                throw new ValidationException("error.event.flashDurationRange", MaxFlashDurationMs);
        }

        // JSON "soundEffects": null overwrites the C# default.
        evt.SoundEffects ??= [];

        if (evt.Timeline is { } timeline)
        {
            ValidateTimeline(timeline);
            // A timeline replaces the legacy flash/sounds — the trigger ignores them, so don't let both
            // be saved (they'd silently do nothing on trigger).
            if ((timeline.Sounds.Count > 0 || timeline.Lights.Count > 0)
                && (evt.Flash is not null || evt.SoundEffects.Count > 0))
                throw new ValidationException("error.event.timelineExclusive");
        }

        if (evt.After is { } after)
        {
            after.Mode = (after.Mode ?? "").Trim().ToLowerInvariant();
            if (after.Mode.Length == 0) after.Mode = "previous";
            if (after.Mode is not ("previous" or "scene" or "default"))
                throw new ValidationException("error.event.afterMode");
            if (after.Mode == "scene")
            {
                // The scene needn't still exist (a deleted target falls back to restoring at trigger time,
                // like a dangling sound id) — just require an id was chosen.
                if (string.IsNullOrWhiteSpace(after.SceneId))
                    throw new ValidationException("error.event.afterSceneId");
            }
            else
            {
                after.SceneId = null; // keep the stored shape honest for non-scene endings
            }
        }
    }

    private static void ValidateTimeline(EventTimeline timeline)
    {
        // JSON "sounds": null / "lights": null overwrite the C# defaults.
        timeline.Sounds ??= [];
        timeline.Lights ??= [];

        if (timeline.Sounds.Count == 0 && timeline.Lights.Count == 0)
            throw new ValidationException("error.event.timelineEmpty");

        var endMs = 0;

        foreach (var clip in timeline.Sounds)
        {
            if (string.IsNullOrWhiteSpace(clip.SoundId))
                throw new ValidationException("error.event.soundClipId");
            // Bound StartMs even for duration-less clips: an unbounded start would park the runner for days
            // and StartMs + DurationMs could overflow int.
            if (clip.StartMs is < 0 or > MaxTimelineMs)
                throw new ValidationException("error.event.soundClipStart", clip.SoundId, MaxTimelineMs);
            if (clip.DurationMs is { } sd && sd is < MinClipDurationMs or > MaxTimelineMs)
                throw new ValidationException("error.event.soundClipDuration", clip.SoundId, MinClipDurationMs, MaxTimelineMs);
            if (clip.Volume is { } vol && vol is < 0.0 or > 1.0)
                throw new ValidationException("error.event.soundClipVolume", clip.SoundId);
            // A duration-less clip plays to its natural end; only bound the clips whose end we know.
            if (clip.DurationMs is { } dur)
                endMs = Math.Max(endMs, clip.StartMs + dur);
        }

        foreach (var clip in timeline.Lights)
        {
            // The context fragment names which light: "all lights" or "light 'key'". The timeline-prefixed
            // variant is used when delegating to ValidateEffect (its messages read "on timeline …").
            var all = string.IsNullOrWhiteSpace(clip.LightKey);
            var ctx = all ? CtxRef.AllLights() : CtxRef.Light(clip.LightKey!);
            if (clip.StartMs is < 0 or > MaxTimelineMs)
                throw new ValidationException("error.event.lightClipStart", ctx, MaxTimelineMs);
            if (clip.DurationMs is < MinClipDurationMs or > MaxTimelineMs)
                throw new ValidationException("error.event.lightClipDuration", ctx, MinClipDurationMs, MaxTimelineMs);
            if (clip.Brightness is < 0 or > 100)
                throw new ValidationException("error.event.lightClipBrightness", ctx);
            if (clip.Temperature is < 0 or > 100)
                throw new ValidationException("error.event.lightClipTemperature", ctx);
            if (clip.Color is not null)
                clip.Color = LightValidation.NormalizeHex(clip.Color);
            if (clip.Effect is { } fx)
                LightValidation.ValidateEffect(fx, all ? CtxRef.TimelineAllLights() : CtxRef.TimelineLight(clip.LightKey!));
            endMs = Math.Max(endMs, clip.StartMs + clip.DurationMs);
        }

        if (endMs > MaxTimelineMs)
            throw new ValidationException("error.event.timelineTooLong", MaxTimelineMs);
    }
}
