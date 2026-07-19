using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Validation;

/// <summary>Shared light-related validation helpers.</summary>
public static class LightValidation
{
    public static bool IsSlug(string s) => s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Validate a light effect and normalize its colors in place. Shared by scene per-light
    /// effects and event timeline light clips. <paramref name="context"/> names the owner in messages.</summary>
    public static void ValidateEffect(LightEffect fx, CtxRef context)
    {
        fx.Colors ??= [];
        fx.Keyframes ??= [];
        if (fx.Type is not ("flicker" or "glow" or "storm" or "drift" or "custom" or "fx"))
            throw new ValidationException("error.effect.unknownType", fx.Type, context);
        if (fx.Type == "fx")
        {
            // A live reference to a library Light FX: needs a valid slug id; the embedded keyframe fields are
            // ignored (resolved from the library at apply time), so clear them for a clean stored shape.
            if (string.IsNullOrWhiteSpace(fx.FxId) || !IsSlug(fx.FxId))
                throw new ValidationException("error.effect.fxIdRequired", context);
            fx.Keyframes = [];
            fx.Loop = false;
            fx.CycleMs = null;
            return;
        }
        if (fx.Type == "custom")
        {
            // An embedded keyframe sequence carries no library reference.
            fx.FxId = null;
            ValidateCustom(fx, context);
            return;
        }

        if (fx.Speed is < 1 or > 10)
            throw new ValidationException("error.effect.speedRange", context);
        if (fx.Intensity is < 1 or > 10)
            throw new ValidationException("error.effect.intensityRange", context);
        for (var i = 0; i < fx.Colors.Count; i++)
            fx.Colors[i] = NormalizeHex(fx.Colors[i]);
        if (fx.Type == "drift" && fx.Colors.Count < 2)
            throw new ValidationException("error.effect.driftColors", context);
    }

    private const int MaxCycleMs = 600_000; // 10 minutes, matching the timeline cap.

    // "custom" keyframe sequence: normalize the effect's keyframes/loop/cycle in place.
    private static void ValidateCustom(LightEffect fx, CtxRef context) =>
        fx.CycleMs = ValidateKeyframes(fx.Keyframes, fx.Loop, fx.CycleMs, context);

    /// <summary>Validate a hand-authored keyframe sequence (shared by "custom" scene/timeline effects and the
    /// reusable Light FX library): 1-50 keyframes at strictly-ascending offsets ≥100 ms apart, each setting at
    /// least one property; colours are normalized in place. Returns the cycle length to persist — the given
    /// value (validated ≥100 ms past the last keyframe) when looping, else null (cycle is meaningless without
    /// looping, so a stale value is dropped).</summary>
    public static int? ValidateKeyframes(List<LightKeyframe> kfs, bool loop, int? cycleMs, CtxRef context)
    {
        if (kfs.Count is < 1 or > 50)
            throw new ValidationException("error.keyframe.count", context);

        var prev = int.MinValue;
        for (var i = 0; i < kfs.Count; i++)
        {
            var kf = kfs[i];
            if (kf.AtMs is < 0 or > MaxCycleMs)
                throw new ValidationException("error.keyframe.startRange", i + 1, context, MaxCycleMs);
            if (i > 0)
            {
                if (kf.AtMs <= prev)
                    throw new ValidationException("error.keyframe.ascending", context);
                if (kf.AtMs - prev < 100)
                    throw new ValidationException("error.keyframe.spacing", context);
            }
            prev = kf.AtMs;

            if (kf.Brightness is < 0 or > 100)
                throw new ValidationException("error.keyframe.brightnessRange", i + 1, context);
            if (kf.Temperature is < 0 or > 100)
                throw new ValidationException("error.keyframe.temperatureRange", i + 1, context);
            if (kf.Color is not null)
                kf.Color = NormalizeHex(kf.Color);
            if (kf.TransitionMs is { } t && t is < 0 or > 60_000)
                throw new ValidationException("error.keyframe.transitionRange", i + 1, context);
            if (kf.Power is null && kf.Color is null && kf.Brightness is null && kf.Temperature is null)
                throw new ValidationException("error.keyframe.empty", i + 1, context);
        }

        var lastAt = kfs[^1].AtMs;
        if (!loop)
            return null; // meaningless without looping — don't persist a stale value.

        if (cycleMs is not { } cycle)
            throw new ValidationException("error.keyframe.cycleRequired", context);
        if (cycle < lastAt + 100)
            throw new ValidationException("error.keyframe.cycleTooShort", context, lastAt + 100);
        if (cycle > MaxCycleMs)
            throw new ValidationException("error.keyframe.cycleTooLong", context, MaxCycleMs);
        return cycle;
    }

    // Accept #RGB or #RRGGBB (leading # optional) and store the canonical "#RRGGBB" the light services parse.
    public static string NormalizeHex(string raw)
    {
        var s = raw.Trim().TrimStart('#');
        if (s.Length == 3 && s.All(Uri.IsHexDigit))
            s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length != 6 || !s.All(Uri.IsHexDigit))
            throw new ValidationException("error.common.hexColor", raw);
        return "#" + s.ToUpperInvariant();
    }
}
