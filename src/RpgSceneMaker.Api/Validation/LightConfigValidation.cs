using RpgSceneMaker.Api.Contracts;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Guards the light registry coming from the Settings page before it reaches the store.</summary>
public static class LightConfigValidation
{
    public static void Validate(List<RegisteredLightDto>? lights)
    {
        if (lights is null) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lights)
        {
            if (string.IsNullOrWhiteSpace(l.Key) || !LightValidation.IsSlug(l.Key))
                throw new ArgumentException($"Light key '{l.Key}' must be a non-empty slug ([a-z0-9-_]).");
            if (!seen.Add(l.Key))
                throw new ArgumentException($"Duplicate light key '{l.Key}'. Keys must be unique (case-insensitive).");
            if (l.Provider?.ToLowerInvariant() is not ("tuya" or "hue"))
                throw new ArgumentException($"Light '{l.Key}' provider must be 'tuya' or 'hue'.");
            if (l.Provider.Equals("hue", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(l.HueId))
                throw new ArgumentException($"Hue light '{l.Key}' needs a HueId.");
        }
    }

    /// <summary>Guards the default light state; returns it with the colour normalised (or null when unset).</summary>
    public static DefaultLightDto? ValidateDefault(DefaultLightDto? d)
    {
        if (d is null) return null;
        if (d.Brightness is < 0 or > 100)
            throw new ArgumentException("Default light brightness must be between 0 and 100.");
        if (d.Temperature is < 0 or > 100)
            throw new ArgumentException("Default light temperature must be between 0 and 100.");
        return d.Color is null ? d : d with { Color = LightValidation.NormalizeHex(d.Color) };
    }
}
