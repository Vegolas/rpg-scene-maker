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
            throw new ArgumentException("Screen id is required.");
        if (!LightValidation.IsSlug(screen.Id))
            throw new ArgumentException("Screen id may only contain letters, digits, '-' and '_'.");
        if (string.IsNullOrWhiteSpace(screen.Name))
            throw new ArgumentException("Screen name is required.");
        if (screen.Image is not null && !ImageFileStorage.IsValidName(screen.Image))
            throw new ArgumentException("Invalid image reference.");

        // JSON "tiles": null overwrites the C# default.
        screen.Tiles ??= [];
        if (screen.Tiles.Count > MaxTiles)
            throw new ArgumentException($"A screen can hold at most {MaxTiles} shortcuts.");

        foreach (var tile in screen.Tiles)
        {
            if (!Kinds.Contains(tile.Kind))
                throw new ArgumentException(
                    $"Unknown tile kind '{tile.Kind}'. Expected one of: {string.Join(", ", Kinds)}.");

            switch (tile.Kind)
            {
                case "light-reset":
                case "break":
                    // A built-in command / layout marker with no target — normalise away any stray ref.
                    tile.Ref = "";
                    break;
                case "music":
                    if (string.IsNullOrWhiteSpace(tile.Ref) || !SpotifyClient.IsSpotifyUri(tile.Ref))
                        throw new ArgumentException(
                            $"Music tile needs a Spotify URI/link, got '{tile.Ref}'.");
                    if (string.IsNullOrWhiteSpace(tile.Label))
                        throw new ArgumentException("Music tile needs a label (the playlist/track name).");
                    break;
                default: // scene / event / sound — a reference to an existing entity by id.
                    if (string.IsNullOrWhiteSpace(tile.Ref))
                        throw new ArgumentException($"A '{tile.Kind}' tile needs the id of the {tile.Kind} it points at.");
                    break;
            }

            tile.Label ??= "";
        }
    }
}
