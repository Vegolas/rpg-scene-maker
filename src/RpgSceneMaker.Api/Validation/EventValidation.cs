using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Guards an event coming from the editor before it reaches the store; failures map to HTTP 400.</summary>
public static class EventValidation
{
    // A flash held longer than this stops feeling like a one-shot; also bounds the light-restore delay.
    private const int MaxFlashDurationMs = 10_000;

    public static void Validate(GameEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Id))
            throw new ArgumentException("Event id is required.");
        if (!LightValidation.IsSlug(evt.Id))
            throw new ArgumentException("Event id may only contain letters, digits, '-' and '_'.");
        if (string.IsNullOrWhiteSpace(evt.Name))
            throw new ArgumentException("Event name is required.");

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
    }
}
