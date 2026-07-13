using RpgSceneMaker.Api.Errors;
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
            throw new ValidationException("error.common.idRequired");
        if (!LightValidation.IsSlug(fx.Id))
            throw new ValidationException("error.common.idSlug");
        if (ReservedIds.Contains(fx.Id, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("error.lightfx.reservedId");
        if (string.IsNullOrWhiteSpace(fx.Name))
            throw new ValidationException("error.common.nameRequired");

        fx.Keyframes ??= [];
        // Reuse the shared "custom"-effect keyframe rules; returns the cycle to persist (null when not looping).
        fx.CycleMs = LightValidation.ValidateKeyframes(fx.Keyframes, fx.Loop, fx.CycleMs, CtxRef.LightFx(fx.Id));
    }
}
