using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        // Nothing is mapped at the bare "/events" path: the Blazor panel's Events tab lives there, so a
        // full-page load of /events must fall through to index.html (same reason /lights uses /lights/list).
        var events = app.MapGroup("/events");

        // Literal segments, so they win over the "/{id}" route below.
        events.MapGet("/list", (EventStore store) => store.GetAllAsync());

        // Stop the running timeline (if any). Command endpoint → GET and POST.
        events.MapMethods("/stop", EndpointHelpers.GetOrPost, (EventTimelineRunner runner) =>
            new EventStopDto(runner.Stop()));

        // Which timeline is running, for the panel's running highlight.
        events.MapGet("/state", (EventTimelineRunner runner) => new EventStateDto(runner.RunningEventId));

        events.MapGet("/{id}", async (string id, EventStore store) =>
            await store.GetAsync(id) is { } evt ? Results.Ok(evt) : Results.NotFound());

        events.MapPut("/{id}", async (string id, GameEvent evt, EventStore store, ImageFileStorage images) =>
        {
            evt.Id = id;
            EventValidation.Validate(evt);
            var oldImage = (await store.GetAsync(id))?.Image;
            await store.UpsertAsync(evt);
            // Drop the previous tile art if it was replaced or cleared, so old uploads don't pile up on disk.
            if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, evt.Image, StringComparison.OrdinalIgnoreCase))
                images.Delete(oldImage);
            return Results.Ok(evt);
        });

        events.MapDelete("/{id}", async (string id, EventStore store, ImageFileStorage images, ScreenStore screens) =>
        {
            var image = (await store.GetAsync(id))?.Image;
            if (!await store.DeleteAsync(id)) return Results.NotFound();
            images.Delete(image);
            // Drop any shortcut tile that pointed at the now-gone event so it doesn't dangle on a board.
            await ReferenceScrubber.ScrubScreenTilesAsync(screens, "event", id);
            return Results.NoContent();
        });

        // Trigger dispatches inside EventActivator (a non-empty timeline runs in the background and returns
        // "started"/"skipped" at once; else the legacy flash + sounds are awaited). 207 if any part failed.
        events.MapMethods("/{id}/trigger", EndpointHelpers.GetOrPost,
            async (string id, EventStore store, EventActivator activator) =>
        {
            if (await store.GetAsync(id) is not { } evt)
                throw new NotFoundException("error.event.notFound", id);

            var result = await activator.TriggerAsync(evt);
            return Results.Json(result, statusCode: result.FullySucceeded ? 200 : 207);
        });
    }
}
