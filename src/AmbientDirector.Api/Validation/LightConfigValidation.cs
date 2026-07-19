using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;

namespace AmbientDirector.Api.Validation;

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
                throw new ValidationException("error.lightConfig.keySlug", l.Key);
            if (!seen.Add(l.Key))
                throw new ValidationException("error.lightConfig.duplicateKey", l.Key);
            if (l.Provider?.ToLowerInvariant() is not ("tuya" or "hue"))
                throw new ValidationException("error.lightConfig.provider", l.Key);
            if (l.Provider.Equals("hue", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(l.HueId))
                throw new ValidationException("error.lightConfig.hueId", l.Key);
        }
    }

    /// <summary>Guards the default light state; returns it with the colour normalised (or null when unset).</summary>
    public static DefaultLightDto? ValidateDefault(DefaultLightDto? d)
    {
        if (d is null) return null;
        if (d.Brightness is < 0 or > 100)
            throw new ValidationException("error.lightConfig.defaultBrightness");
        if (d.Temperature is < 0 or > 100)
            throw new ValidationException("error.lightConfig.defaultTemperature");
        return d.Color is null ? d : d with { Color = LightValidation.NormalizeHex(d.Color) };
    }
}
