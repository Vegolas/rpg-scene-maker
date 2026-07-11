using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Guards a library <see cref="LightFx"/> coming from the editor before it reaches the store;
/// failures map to HTTP 400.</summary>
public static class LightFxValidation
{
    // Ids that would shadow the literal /lightfx/{list,test} and /lightfx/test/stop routes.
    private static readonly string[] ReservedIds = ["list", "test", "stop"];

    public static void Validate(LightFx fx)
    {
        if (string.IsNullOrWhiteSpace(fx.Id))
            throw new ArgumentException("Light FX id is required.");
        if (!LightValidation.IsSlug(fx.Id))
            throw new ArgumentException("Light FX id may only contain letters, digits, '-' and '_'.");
        if (ReservedIds.Contains(fx.Id, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Light FX id 'list', 'test' and 'stop' are reserved (they'd shadow the /lightfx routes).");
        if (string.IsNullOrWhiteSpace(fx.Name))
            throw new ArgumentException("Light FX name is required.");

        fx.Keyframes ??= [];
        // Reuse the shared "custom"-effect keyframe rules; returns the cycle to persist (null when not looping).
        fx.CycleMs = LightValidation.ValidateKeyframes(fx.Keyframes, fx.Loop, fx.CycleMs, $"Light FX '{fx.Id}'");
    }
}
