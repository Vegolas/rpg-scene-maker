using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Guards a scene coming from the editor before it reaches the store; failures map to HTTP 400.</summary>
public static class SceneValidation
{
    public static void Validate(Scene scene)
    {
        if (string.IsNullOrWhiteSpace(scene.Id))
            throw new ArgumentException("Scene id is required.");
        if (!scene.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Scene id may only contain letters, digits, '-' and '_'.");
        if (string.IsNullOrWhiteSpace(scene.Name))
            throw new ArgumentException("Scene name is required.");

        if (scene.Light is { } light)
        {
            if (light.Brightness is < 0 or > 100)
                throw new ArgumentException("Light brightness must be between 0 and 100.");
            if (light.Temperature is < 0 or > 100)
                throw new ArgumentException("Light temperature must be between 0 and 100.");
            if (light.Color is not null)
                light.Color = LightValidation.NormalizeHex(light.Color);
        }

        // JSON "lights": null / "colors": null / "soundEffects": null overwrite the C# defaults.
        scene.Lights ??= [];
        scene.SoundEffects ??= [];

        foreach (var entry in scene.Lights)
        {
            if (string.IsNullOrWhiteSpace(entry.LightKey) || !LightValidation.IsSlug(entry.LightKey))
                throw new ArgumentException("Each scene light needs a LightKey slug ([a-z0-9-_]).");
            if (entry.Brightness is < 0 or > 100)
                throw new ArgumentException($"Light '{entry.LightKey}' brightness must be between 0 and 100.");
            if (entry.Temperature is < 0 or > 100)
                throw new ArgumentException($"Light '{entry.LightKey}' temperature must be between 0 and 100.");
            if (entry.Color is not null)
                entry.Color = LightValidation.NormalizeHex(entry.Color);

            if (entry.Effect is { } fx)
            {
                fx.Colors ??= [];
                if (fx.Type is not ("flicker" or "glow" or "storm" or "drift"))
                    throw new ArgumentException($"Unknown effect type '{fx.Type}' on light '{entry.LightKey}'. Use flicker, glow, storm or drift.");
                if (fx.Speed is < 1 or > 10)
                    throw new ArgumentException($"Effect speed on light '{entry.LightKey}' must be between 1 and 10.");
                if (fx.Intensity is < 1 or > 10)
                    throw new ArgumentException($"Effect intensity on light '{entry.LightKey}' must be between 1 and 10.");
                for (var i = 0; i < fx.Colors.Count; i++)
                    fx.Colors[i] = LightValidation.NormalizeHex(fx.Colors[i]);
                if (fx.Type == "drift" && fx.Colors.Count < 2)
                    throw new ArgumentException($"The 'drift' effect on light '{entry.LightKey}' needs at least 2 colors.");
            }
        }

        if (scene.Music is { } music)
        {
            if (music.Volume is { } volume && volume is < 0.0 or > 1.0)
                throw new ArgumentException("Music volume must be between 0.0 and 1.0.");
            if (!string.IsNullOrWhiteSpace(music.PlayId) && !SpotifyClient.IsSpotifyUri(music.PlayId))
                throw new ArgumentException(
                    $"Music id must be a Spotify link/URI (e.g. spotify:playlist:… or an open.spotify.com link): '{music.PlayId}'.");
        }
    }
}
