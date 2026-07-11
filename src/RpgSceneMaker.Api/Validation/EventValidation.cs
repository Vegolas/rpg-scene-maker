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
            throw new ArgumentException("Event id is required.");
        if (!LightValidation.IsSlug(evt.Id))
            throw new ArgumentException("Event id may only contain letters, digits, '-' and '_'.");
        if (ReservedIds.Contains(evt.Id, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Event id 'list', 'stop' and 'state' are reserved (they'd shadow the /events/list, /events/stop and /events/state routes).");
        if (string.IsNullOrWhiteSpace(evt.Name))
            throw new ArgumentException("Event name is required.");
        if (evt.Image is not null && !ImageFileStorage.IsValidName(evt.Image))
            throw new ArgumentException("Invalid image reference.");

        if (evt.Flash is { } flash)
        {
            flash.Color = LightValidation.NormalizeHex(flash.Color);
            if (flash.Brightness is < 0 or > 100)
                throw new ArgumentException("Flash brightness must be between 0 and 100.");
            if (flash.DurationMs is < 1 or > MaxFlashDurationMs)
                throw new ArgumentException($"Flash duration must be between 1 and {MaxFlashDurationMs} ms.");
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
                throw new ArgumentException("An event with timeline clips can't also set a flash or sound effects — the timeline replaces them.");
        }
    }

    private static void ValidateTimeline(EventTimeline timeline)
    {
        // JSON "sounds": null / "lights": null overwrite the C# defaults.
        timeline.Sounds ??= [];
        timeline.Lights ??= [];

        if (timeline.Sounds.Count == 0 && timeline.Lights.Count == 0)
            throw new ArgumentException("A timeline needs at least one sound or light clip.");

        var endMs = 0;

        foreach (var clip in timeline.Sounds)
        {
            if (string.IsNullOrWhiteSpace(clip.SoundId))
                throw new ArgumentException("Each timeline sound clip needs a soundId.");
            // Bound StartMs even for duration-less clips: an unbounded start would park the runner for days
            // and StartMs + DurationMs could overflow int.
            if (clip.StartMs is < 0 or > MaxTimelineMs)
                throw new ArgumentException($"Timeline sound clip '{clip.SoundId}' start must be between 0 and {MaxTimelineMs} ms.");
            if (clip.DurationMs is { } sd && sd is < MinClipDurationMs or > MaxTimelineMs)
                throw new ArgumentException($"Timeline sound clip '{clip.SoundId}' duration must be between {MinClipDurationMs} and {MaxTimelineMs} ms.");
            if (clip.Volume is { } vol && vol is < 0.0 or > 1.0)
                throw new ArgumentException($"Timeline sound clip '{clip.SoundId}' volume must be between 0.0 and 1.0.");
            // A duration-less clip plays to its natural end; only bound the clips whose end we know.
            if (clip.DurationMs is { } dur)
                endMs = Math.Max(endMs, clip.StartMs + dur);
        }

        foreach (var clip in timeline.Lights)
        {
            var name = string.IsNullOrWhiteSpace(clip.LightKey) ? "all lights" : $"light '{clip.LightKey}'";
            if (clip.StartMs is < 0 or > MaxTimelineMs)
                throw new ArgumentException($"Timeline light clip ({name}) start must be between 0 and {MaxTimelineMs} ms.");
            if (clip.DurationMs is < MinClipDurationMs or > MaxTimelineMs)
                throw new ArgumentException($"Timeline light clip ({name}) duration must be between {MinClipDurationMs} and {MaxTimelineMs} ms.");
            if (clip.Brightness is < 0 or > 100)
                throw new ArgumentException($"Timeline light clip ({name}) brightness must be between 0 and 100.");
            if (clip.Temperature is < 0 or > 100)
                throw new ArgumentException($"Timeline light clip ({name}) temperature must be between 0 and 100.");
            if (clip.Color is not null)
                clip.Color = LightValidation.NormalizeHex(clip.Color);
            if (clip.Effect is { } fx)
                LightValidation.ValidateEffect(fx, $"timeline {name}");
            endMs = Math.Max(endMs, clip.StartMs + clip.DurationMs);
        }

        if (endMs > MaxTimelineMs)
            throw new ArgumentException($"The timeline must end within {MaxTimelineMs} ms (10 minutes).");
    }
}
