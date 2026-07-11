using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Endpoints;

public static class ScreenEndpoints
{
    public static void MapScreenEndpoints(this WebApplication app)
    {
        // A screen is an organizational board of shortcuts, not an action, so there is no /trigger here —
        // its tiles just call the existing /scenes, /events, /sounds, /music and /lights endpoints.
        var screens = app.MapGroup("/screens");

        // Literal segment, so it wins over any "/{id}" route.
        screens.MapGet("/list", (ScreenStore store) => store.GetAllAsync());

        screens.MapPut("/{id}", async (string id, Screen screen, ScreenStore store, ImageFileStorage images) =>
        {
            screen.Id = id;
            ScreenValidation.Validate(screen);
            var oldImage = (await store.GetAsync(id))?.Image;
            await store.UpsertAsync(screen);
            // Drop the previous tile art if it was replaced or cleared, so old uploads don't pile up on disk.
            if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, screen.Image, StringComparison.OrdinalIgnoreCase))
                images.Delete(oldImage);
            return Results.Ok(screen);
        });

        screens.MapDelete("/{id}", async (string id, ScreenStore store, ImageFileStorage images) =>
        {
            var image = (await store.GetAsync(id))?.Image;
            if (!await store.DeleteAsync(id)) return Results.NotFound();
            images.Delete(image);
            return Results.NoContent();
        });

        // Deliberately NO "GET /screens/{id}" (and nothing at the bare "/screens"): the Blazor panel's
        // Screens list lives at /screens and each board at /screens/{id}, so a full-page load of either
        // must fall through to index.html. The panel reads the whole list from /screens/list and picks the
        // board by id client-side. (MapFallbackToFile only serves GET, so the PUT/DELETE above are safe.)
    }
}
