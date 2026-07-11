using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Shared light-related validation helpers.</summary>
public static class LightValidation
{
    public static bool IsSlug(string s) => s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Validate a light effect and normalize its colors in place. Shared by scene per-light
    /// effects and event timeline light clips. <paramref name="context"/> names the owner in messages.</summary>
    public static void ValidateEffect(LightEffect fx, string context)
    {
        fx.Colors ??= [];
        fx.Keyframes ??= [];
        if (fx.Type is not ("flicker" or "glow" or "storm" or "drift" or "custom"))
            throw new ArgumentException($"Unknown effect type '{fx.Type}' on {context}. Use flicker, glow, storm, drift or custom.");
        if (fx.Type == "custom")
        {
            ValidateCustom(fx, context);
            return;
        }

        if (fx.Speed is < 1 or > 10)
            throw new ArgumentException($"Effect speed on {context} must be between 1 and 10.");
        if (fx.Intensity is < 1 or > 10)
            throw new ArgumentException($"Effect intensity on {context} must be between 1 and 10.");
        for (var i = 0; i < fx.Colors.Count; i++)
            fx.Colors[i] = NormalizeHex(fx.Colors[i]);
        if (fx.Type == "drift" && fx.Colors.Count < 2)
            throw new ArgumentException($"The 'drift' effect on {context} needs at least 2 colors.");
    }

    private const int MaxCycleMs = 600_000; // 10 minutes, matching the timeline cap.

    // "custom" keyframe sequence: 1-50 keyframes at strictly-ascending offsets ≥100 ms apart, each setting
    // at least one property. Loop needs a CycleMs at least 100 ms past the last keyframe; non-loop clears it.
    private static void ValidateCustom(LightEffect fx, string context)
    {
        var kfs = fx.Keyframes;
        if (kfs.Count is < 1 or > 50)
            throw new ArgumentException($"The 'custom' effect on {context} needs between 1 and 50 keyframes.");

        var prev = int.MinValue;
        for (var i = 0; i < kfs.Count; i++)
        {
            var kf = kfs[i];
            if (kf.AtMs is < 0 or > MaxCycleMs)
                throw new ArgumentException($"Keyframe {i + 1} on {context} must start between 0 and {MaxCycleMs} ms.");
            if (i > 0)
            {
                if (kf.AtMs <= prev)
                    throw new ArgumentException($"Keyframes on {context} must be in ascending time order.");
                if (kf.AtMs - prev < 100)
                    throw new ArgumentException($"Keyframes on {context} must be at least 100 ms apart.");
            }
            prev = kf.AtMs;

            if (kf.Brightness is < 0 or > 100)
                throw new ArgumentException($"Keyframe {i + 1} on {context} brightness must be between 0 and 100.");
            if (kf.Temperature is < 0 or > 100)
                throw new ArgumentException($"Keyframe {i + 1} on {context} temperature must be between 0 and 100.");
            if (kf.Color is not null)
                kf.Color = NormalizeHex(kf.Color);
            if (kf.TransitionMs is { } t && t is < 0 or > 60_000)
                throw new ArgumentException($"Keyframe {i + 1} on {context} transition must be between 0 and 60000 ms.");
            if (kf.Power is null && kf.Color is null && kf.Brightness is null && kf.Temperature is null)
                throw new ArgumentException($"Keyframe {i + 1} on {context} must set power, color, brightness or temperature.");
        }

        var lastAt = kfs[^1].AtMs;
        if (fx.Loop)
        {
            if (fx.CycleMs is not { } cycle)
                throw new ArgumentException($"The looping 'custom' effect on {context} needs a cycle length.");
            if (cycle < lastAt + 100)
                throw new ArgumentException($"The cycle length on {context} must be at least {lastAt + 100} ms (100 ms past the last keyframe).");
            if (cycle > MaxCycleMs)
                throw new ArgumentException($"The cycle length on {context} must be at most {MaxCycleMs} ms.");
        }
        else
        {
            fx.CycleMs = null; // meaningless without looping — don't persist a stale value.
        }
    }

    // Accept #RGB or #RRGGBB (leading # optional) and store the canonical "#RRGGBB" the light services parse.
    public static string NormalizeHex(string raw)
    {
        var s = raw.Trim().TrimStart('#');
        if (s.Length == 3 && s.All(Uri.IsHexDigit))
            s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length != 6 || !s.All(Uri.IsHexDigit))
            throw new ArgumentException($"'{raw}' is not a valid hex color. Use #RGB or #RRGGBB, e.g. #FF8C2A.");
        return "#" + s.ToUpperInvariant();
    }
}
