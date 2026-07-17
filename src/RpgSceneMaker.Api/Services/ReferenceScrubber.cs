namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Scrubs cross-entity references when a scene/event/sound is deleted, so nothing is left dangling. Screens
/// hold shortcut tiles that point at those entities by id, and an event's <c>After</c> can activate a scene by
/// id; deleting the target used to leave a permanent "missing" tile / a dangling After. Shared by the delete
/// endpoints and the AI tool façade so both delete the same way. Mirrors how deleting a sound scrubs its id
/// from scenes/events (<see cref="Endpoints.SoundEndpoints"/>) and how <see cref="LightFxDetacher"/> detaches
/// FX references.
/// </summary>
public static class ReferenceScrubber
{
    /// <summary>Remove every screen tile that points at the deleted entity, matched by kind + id
    /// (case-insensitively, like the stores' NOCASE ids). Only the screens actually touched are saved.
    /// <paramref name="kind"/> is the <see cref="Models.ScreenTile.Kind"/> — <c>scene</c>/<c>event</c>/<c>sound</c>;
    /// music tiles carry a Spotify URI, not an entity id, so they are never matched.</summary>
    public static async Task ScrubScreenTilesAsync(ScreenStore screens, string kind, string id)
    {
        foreach (var screen in await screens.GetAllAsync())
        {
            var kept = screen.Tiles
                .Where(t => !(string.Equals(t.Kind, kind, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(t.Ref, id, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (kept.Count != screen.Tiles.Count)
            {
                screen.Tiles = kept;
                await screens.UpsertAsync(screen);
            }
        }
    }

    /// <summary>Reset any event whose <c>After</c> activates the now-deleted scene back to the historical
    /// default (restore prior lighting) by clearing <see cref="Models.GameEvent.After"/> — null and Mode
    /// "previous" are equivalent. Only the events actually touched are saved.</summary>
    public static async Task ScrubEventAfterSceneAsync(EventStore events, string sceneId)
    {
        foreach (var evt in await events.GetAllAsync())
        {
            if (evt.After is { Mode: "scene" } after
                && string.Equals(after.SceneId, sceneId, StringComparison.OrdinalIgnoreCase))
            {
                evt.After = null;
                await events.UpsertAsync(evt);
            }
        }
    }
}
