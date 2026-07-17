using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Endpoints;

public static class SceneEndpoints
{
    public static void MapSceneEndpoints(this WebApplication app)
    {
        var scenes = app.MapGroup("/scenes");

        scenes.MapGet("/", (SceneStore store) => store.GetAllAsync());

        scenes.MapGet("/active", (CurrentState state) =>
            new { id = state.ActiveSceneId, activatedAt = state.ActivatedAt });

        scenes.MapGet("/{id}", async (string id, SceneStore store) =>
            await store.GetAsync(id) is { } scene ? Results.Ok(scene) : Results.NotFound());

        scenes.MapPut("/{id}", async (string id, Scene scene, SceneStore store, ImageFileStorage images) =>
        {
            scene.Id = id;
            SceneValidation.Validate(scene);
            var oldImage = (await store.GetAsync(id))?.Image;
            await store.UpsertAsync(scene);
            // Drop the previous tile art if it was replaced or cleared, so old uploads don't pile up on disk.
            if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, scene.Image, StringComparison.OrdinalIgnoreCase))
                images.Delete(oldImage);
            return Results.Ok(scene);
        });

        scenes.MapDelete("/{id}", async (string id, SceneStore store, ImageFileStorage images,
            ScreenStore screens, EventStore events, CurrentState state) =>
        {
            var image = (await store.GetAsync(id))?.Image;
            if (!await store.DeleteAsync(id)) return Results.NotFound();
            images.Delete(image);
            // Scrub everything that referenced the now-gone scene so nothing dangles: shortcut tiles, any
            // event that activated it via After, and the "currently showing" highlight.
            await ReferenceScrubber.ScrubScreenTilesAsync(screens, "scene", id);
            await ReferenceScrubber.ScrubEventAfterSceneAsync(events, id);
            if (string.Equals(state.ActiveSceneId, id, StringComparison.OrdinalIgnoreCase))
            {
                state.ActiveSceneId = null;
                state.ActivatedAt = null;
            }
            return Results.NoContent();
        });

        scenes.MapMethods("/{id}/activate", EndpointHelpers.GetOrPost, async (string id, SceneStore store, SceneActivator activator) =>
        {
            if (await store.GetAsync(id) is not { } scene)
                return Results.NotFound(new { error = $"No scene with id '{id}'. See GET /scenes." });
            var result = await activator.ActivateAsync(scene);
            return Results.Json(result, statusCode: result.FullySucceeded ? 200 : 207);
        });
    }
}
