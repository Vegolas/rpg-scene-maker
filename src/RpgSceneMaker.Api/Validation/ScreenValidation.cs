using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Validation;

/// <summary>Guards a screen coming from the editor before it reaches the store; failures map to HTTP 400.</summary>
public static class ScreenValidation
{
    // A board with more than this many shortcuts stops being tap-friendly; also bounds the JSON payload.
    private const int MaxTiles = 100;

    // The tile kinds the panel knows how to render and operate. "break" is a layout-only marker (a
    // full-width line break, optionally labelled to act as a section heading) — it has no target.
    private static readonly HashSet<string> Kinds =
        new(StringComparer.Ordinal) { "scene", "event", "sound", "music", "light-reset", "break" };

    public static void Validate(Screen screen)
    {
        if (string.IsNullOrWhiteSpace(screen.Id))
            throw new ValidationException("error.common.idRequired");
        if (!LightValidation.IsSlug(screen.Id))
            throw new ValidationException("error.common.idSlug");
        if (string.IsNullOrWhiteSpace(screen.Name))
            throw new ValidationException("error.common.nameRequired");
        if (screen.Image is not null && !ImageFileStorage.IsValidName(screen.Image))
            throw new ValidationException("error.common.invalidImage");

        // JSON "tiles": null overwrites the C# default.
        screen.Tiles ??= [];
        if (screen.Tiles.Count > MaxTiles)
            throw new ValidationException("error.screen.tooManyTiles", MaxTiles);

        foreach (var tile in screen.Tiles)
        {
            if (!Kinds.Contains(tile.Kind))
                throw new ValidationException("error.screen.unknownKind", tile.Kind, string.Join(", ", Kinds));

            switch (tile.Kind)
            {
                case "light-reset":
                case "break":
                    // A built-in command / layout marker with no target — normalise away any stray ref.
                    tile.Ref = "";
                    break;
                case "music":
                    if (string.IsNullOrWhiteSpace(tile.Ref) || !SpotifyClient.IsSpotifyUri(tile.Ref))
                        throw new ValidationException("error.screen.musicTileUri", tile.Ref);
                    if (string.IsNullOrWhiteSpace(tile.Label))
                        throw new ValidationException("error.screen.musicTileLabel");
                    break;
                default: // scene / event / sound — a reference to an existing entity by id.
                    if (string.IsNullOrWhiteSpace(tile.Ref))
                        throw new ValidationException("error.screen.tileRefRequired", tile.Kind);
                    break;
            }

            tile.Label ??= "";
        }
    }
}
