using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Endpoints;

public static class EncounterEndpoints
{
    public static void MapEncounterEndpoints(this WebApplication app)
    {
        // An encounter is a prepped fight (heroes + enemy instances + background + optional scene/event) the GM
        // runs at the table (issue #122). Running it activates the scene/event best-effort and pushes the
        // heroes-left / enemies-right view to /tv. Follows the /boards + /party house pattern: nothing at the
        // bare "/encounters", no GET /encounters/{id}, commands are GET+POST.
        var encounters = app.MapGroup("/encounters");

        // Literal segment, so it wins over any "/{id}" route.
        encounters.MapGet("/list", (EncounterStore store) => store.GetAllAsync());

        encounters.MapPut("/{id}", async (string id, Encounter encounter, EncounterStore store,
            ImageFileStorage images, TvState tvState) =>
        {
            encounter.Id = id;
            EncounterValidation.Validate(encounter);
            // Own the encounter's background upload and clean up on replace (the single-image ownership pattern):
            // capture the old file before the upsert, drop it afterwards if it changed. Instance portraits are
            // snapshots of the bestiary template's file (owned there), so they are deliberately not released here.
            var oldBackground = (await store.GetAsync(id))?.BackgroundImage;
            await store.UpsertAsync(encounter);
            if (!string.IsNullOrEmpty(oldBackground) &&
                !string.Equals(oldBackground, encounter.BackgroundImage, StringComparison.OrdinalIgnoreCase))
                images.Delete(oldBackground);
            // Live edit: if this encounter is the one currently on the TV, bump the rev so an open display
            // re-renders within one 2 s poll (the codebase's real-time idiom — no SSE).
            tvState.TouchEncounter(id);
            return Results.Ok(encounter);
        });

        encounters.MapDelete("/{id}", async (string id, EncounterStore store,
            ImageFileStorage images, TvState tvState) =>
        {
            // Capture the owned background before deleting so we can release it afterwards.
            var background = (await store.GetAsync(id))?.BackgroundImage;
            if (!await store.DeleteAsync(id)) return Results.NotFound();
            images.Delete(background);
            // Nothing dangles after a delete: clear the display if this encounter was showing, and scrub it from
            // Recent (mirrors the board-delete / sound-delete scrub).
            tvState.ForgetEncounter(id);
            return Results.NoContent();
        });

        // Run the encounter: activate its scene (SceneActivator) and event (EventActivator) best-effort — each
        // reports ok/skipped/notFound/error but never blocks the show — then push it to the TV. GET+POST so a
        // Stream Deck "System → Website" button can run a fight.
        encounters.MapMethods("/{id}/run", EndpointHelpers.GetOrPost, async (string id, EncounterStore store,
            SceneStore scenes, EventStore events, SceneActivator sceneActivator, EventActivator eventActivator,
            TvState tvState) =>
        {
            var encounter = await store.GetAsync(id)
                ?? throw new NotFoundException("error.encounter.notFound", id);
            var sceneStatus = await RunSceneAsync(encounter.ActivateSceneId, scenes, sceneActivator);
            var eventStatus = await RunEventAsync(encounter.ActivateEventId, events, eventActivator);
            var rev = tvState.ShowEncounter(encounter.Id, encounter.Name);
            return Results.Ok(new { rev, encounter = encounter.Id, scene = sceneStatus, @event = eventStatus });
        });

        // Tap-to-adjust one enemy instance's counter (live per-fight tracking). Same shape as the party adjust
        // routes (GET+POST, ?delta= / ?value= XOR), so a Stream Deck button can do
        // /encounters/goblin-ambush/enemies/goblin-1/adjust?counter=HP&delta=-1.
        encounters.MapMethods("/{id}/enemies/{instanceId}/adjust", EndpointHelpers.GetOrPost,
            async (string id, string instanceId, string? counter, int? delta, int? value,
                EncounterStore store, TvState tvState) =>
        {
            if (string.IsNullOrWhiteSpace(counter))
                throw new ValidationException("error.party.adjustCounter");
            if (delta.HasValue == value.HasValue) // both, or neither
                throw new ValidationException("error.party.adjustTarget");
            var updated = await store.AdjustEnemyInstanceAsync(id, instanceId, counter.Trim(), delta, value);
            tvState.TouchEncounter(id);
            return Results.Ok(updated);
        });

        // Reset every enemy instance's counters to full (bounded → Max), before re-running the same fight.
        encounters.MapMethods("/{id}/reset", EndpointHelpers.GetOrPost, async (string id,
            EncounterStore store, TvState tvState) =>
        {
            var updated = await store.ResetEnemiesAsync(id);
            tvState.TouchEncounter(id);
            return Results.Ok(updated);
        });

        // Deliberately NO "GET /encounters/{id}" (and nothing at the bare "/encounters"): the Blazor panel's
        // encounters list lives at /encounters and each editor at /encounters/{id}, so a full-page load of
        // either must fall through to index.html. The panel reads /encounters/list and picks by id client-side.
        // (MapFallbackToFile only serves GET, so the PUT/DELETE above are safe.)
    }

    // Activate a configured scene, best-effort: "skipped" (none set) / "notFound" (since deleted) / "ok" /
    // "partial" (some part errored) / "error:<code>" (activation threw). Never rethrows — running an encounter
    // still shows on the TV even if the ambiance failed (like a scene activation's own 207-tolerant parts).
    private static async Task<string> RunSceneAsync(string? sceneId, SceneStore scenes, SceneActivator activator)
    {
        if (string.IsNullOrWhiteSpace(sceneId)) return "skipped";
        var scene = await scenes.GetAsync(sceneId);
        if (scene is null) return "notFound";
        try
        {
            var result = await activator.ActivateAsync(scene);
            return result.FullySucceeded ? "ok" : "partial";
        }
        catch (Exception ex)
        {
            return "error:" + ErrorClassifier.DisplayCodeFor(ex);
        }
    }

    private static async Task<string> RunEventAsync(string? eventId, EventStore events, EventActivator activator)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return "skipped";
        var evt = await events.GetAsync(eventId);
        if (evt is null) return "notFound";
        try
        {
            var result = await activator.TriggerAsync(evt);
            return result.FullySucceeded ? "ok" : "partial";
        }
        catch (Exception ex)
        {
            return "error:" + ErrorClassifier.DisplayCodeFor(ex);
        }
    }
}
