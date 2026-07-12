using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Endpoints;

public static class LightFxEndpoints
{
    public static void MapLightFxEndpoints(this WebApplication app)
    {
        // The panel's Effects pages live at /effects and /effects/{id} (NOT /lightfx), so a full-page load of
        // either falls through to index.html — /lightfx is purely the API. Like /screens there is deliberately
        // no GET /lightfx/{id} (and nothing at the bare /lightfx): the panel reads the whole library from
        // /lightfx/list and picks by id client-side.
        var fx = app.MapGroup("/lightfx");

        // Literal segment, so it wins over the "/{id}" routes.
        fx.MapGet("/list", (LightFxStore store) => store.GetAllAsync());

        fx.MapPut("/{id}", async (string id, LightFx effect, LightFxStore store) =>
        {
            effect.Id = id;
            LightFxValidation.Validate(effect);
            await store.UpsertAsync(effect);
            return Results.Ok(effect);
        });

        fx.MapDelete("/{id}", async (string id, LightFxStore store, SceneStore scenes, EventStore events) =>
        {
            if (await store.GetAsync(id) is not { } effect) return Results.NotFound();
            // Detach on delete: replace every live "fx" reference (scene lights + event timeline clips) with an
            // embedded "custom" copy of the FX's keyframes, so nothing is left dangling and behaviour is
            // unchanged. Mirrors how deleting a sound scrubs its id from scenes/events.
            await LightFxDetacher.DetachReferencesAsync(effect, scenes, events);
            await store.DeleteAsync(id);
            return Results.NoContent();
        });

        // Bounded test-run of an FX on a light (empty light = the configured provider group). Command endpoint
        // → GET and POST. seconds defaults to 10, clamped 1–60.
        fx.MapMethods("/{id}/test", EndpointHelpers.GetOrPost,
            async (string id, string? light, int? seconds, LightFxStore store, LightFxTester tester) =>
        {
            if (await store.GetAsync(id) is not { } effect)
                return Results.NotFound(new { error = $"No light FX with id '{id}'. See GET /lightfx/list." });
            var window = Math.Clamp(seconds ?? 10, 1, 60);
            await tester.StartAsync(effect, string.IsNullOrWhiteSpace(light) ? null : light, window);
            return Results.Ok(new { testing = effect.Id, seconds = window });
        });

        fx.MapMethods("/test/stop", EndpointHelpers.GetOrPost,
            (LightFxTester tester) => new { stopped = tester.Stop() });
    }
}
