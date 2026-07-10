using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        // Nothing is mapped at the bare "/events" path: the Blazor panel's Events tab lives there, so a
        // full-page load of /events must fall through to index.html (same reason /lights uses /lights/list).
        var events = app.MapGroup("/events");

        // Literal segment, so it wins over the "/{id}" route below.
        events.MapGet("/list", (EventStore store) => store.GetAllAsync());

        events.MapGet("/{id}", async (string id, EventStore store) =>
            await store.GetAsync(id) is { } evt ? Results.Ok(evt) : Results.NotFound());

        events.MapPut("/{id}", async (string id, GameEvent evt, EventStore store) =>
        {
            evt.Id = id;
            EventValidation.Validate(evt);
            await store.UpsertAsync(evt);
            return Results.Ok(evt);
        });

        events.MapDelete("/{id}", async (string id, EventStore store) =>
            await store.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());

        events.MapMethods("/{id}/trigger", EndpointHelpers.GetOrPost, async (string id, EventStore store, EventActivator activator) =>
        {
            if (await store.GetAsync(id) is not { } evt)
                return Results.NotFound(new { error = $"No event with id '{id}'. See GET /events/list." });
            var result = await activator.TriggerAsync(evt);
            return Results.Json(result, statusCode: result.FullySucceeded ? 200 : 207);
        });
    }
}
