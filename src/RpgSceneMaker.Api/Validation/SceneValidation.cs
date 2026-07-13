using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Guards a scene coming from the editor before it reaches the store; failures map to HTTP 400.</summary>
public static class SceneValidation
{
    public static void Validate(Scene scene)
    {
        if (string.IsNullOrWhiteSpace(scene.Id))
            throw new ValidationException("error.common.idRequired");
        if (!scene.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ValidationException("error.common.idSlug");
        if (string.IsNullOrWhiteSpace(scene.Name))
            throw new ValidationException("error.common.nameRequired");
        if (scene.Image is not null && !ImageFileStorage.IsValidName(scene.Image))
            throw new ValidationException("error.common.invalidImage");

        if (scene.Light is { } light)
        {
            if (light.Brightness is < 0 or > 100)
                throw new ValidationException("error.scene.brightnessRange");
            if (light.Temperature is < 0 or > 100)
                throw new ValidationException("error.scene.temperatureRange");
            if (light.Color is not null)
                light.Color = LightValidation.NormalizeHex(light.Color);
        }

        // JSON "lights": null / "colors": null / "soundEffects": null overwrite the C# defaults.
        scene.Lights ??= [];
        scene.SoundEffects ??= [];

        foreach (var entry in scene.Lights)
        {
            if (string.IsNullOrWhiteSpace(entry.LightKey) || !LightValidation.IsSlug(entry.LightKey))
                throw new ValidationException("error.scene.lightKeySlug");
            if (entry.Brightness is < 0 or > 100)
                throw new ValidationException("error.scene.lightBrightnessRange", entry.LightKey);
            if (entry.Temperature is < 0 or > 100)
                throw new ValidationException("error.scene.lightTemperatureRange", entry.LightKey);
            if (entry.Color is not null)
                entry.Color = LightValidation.NormalizeHex(entry.Color);

            if (entry.Effect is { } fx)
                LightValidation.ValidateEffect(fx, CtxRef.Light(entry.LightKey));
        }

        if (scene.Music is { } music)
        {
            if (music.Volume is { } volume && volume is < 0.0 or > 1.0)
                throw new ValidationException("error.scene.musicVolumeRange");
            if (!string.IsNullOrWhiteSpace(music.PlayId) && !SpotifyClient.IsSpotifyUri(music.PlayId))
                throw new ValidationException("error.scene.musicUri", music.PlayId);
        }
    }
}
