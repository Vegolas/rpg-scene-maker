using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Services.Ai;

/// <summary>A registered light, flattened for AI-tool context (key/name/provider only).</summary>
public record LightInfo(string Key, string Name, string Provider);

/// <summary>A sound-effect summary for AI-tool context (no waveform, no file name).</summary>
public record SoundInfo(string Id, string Name, string Category, double Volume, bool Loop, int? DurationMs);

/// <summary>The scene the table is currently showing (null id = none activated yet).</summary>
public record ActiveSceneInfo(string? Id, DateTimeOffset? ActivatedAt);

/// <summary>The running light-FX test (its FX id and the clamped window in seconds).</summary>
public record LightFxTestInfo(string Testing, int Seconds);

/// <summary>
/// Shared tool layer over scenes, events and light FX (plus read-only context and live control), used by
/// both the MCP server and the in-panel assistant (both added in later commits) so the two surfaces behave
/// identically to each other and to the HTTP endpoints they mirror. CRUD goes straight to the stores +
/// existing validators + image cleanup, exactly like the endpoints. Registered as a singleton, but
/// <see cref="SceneActivator"/> / <see cref="EventActivator"/> / <see cref="ILightService"/> /
/// <see cref="SpotifyClient"/> are scoped (the light provider is chosen per-scope from settings), so each
/// activation/trigger/reset/Spotify call creates its own service scope for the operation (the pattern used by
/// <see cref="EventTimelineRunner"/>); <see cref="LightFxTester"/> owns its own scope per test run.
/// </summary>
public sealed class AiToolService(
    IServiceScopeFactory scopeFactory,
    SceneStore scenes,
    EventStore events,
    LightFxStore lightFx,
    SoundStore sounds,
    SettingsStore settings,
    LightRegistry lights,
    CurrentState state,
    EventTimelineRunner timelineRunner,
    LightFxTester fxTester,
    ImageFileStorage images,
    EffectEngine effects)
{
    // ---- Scenes ----

    public Task<List<Scene>> ListScenesAsync() => scenes.GetAllAsync();

    public Task<Scene?> GetSceneAsync(string id) => scenes.GetAsync(id);

    // Mirrors PUT /scenes/{id}: stamp the id, validate, then upsert and drop replaced/cleared tile art.
    public async Task<Scene> UpsertSceneAsync(Scene scene, string id)
    {
        scene.Id = id;
        SceneValidation.Validate(scene);
        var oldImage = (await scenes.GetAsync(id))?.Image;
        await scenes.UpsertAsync(scene);
        if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, scene.Image, StringComparison.OrdinalIgnoreCase))
            images.Delete(oldImage);
        return scene;
    }

    // Mirrors DELETE /scenes/{id}: remove the tile art too. False when the scene didn't exist.
    public async Task<bool> DeleteSceneAsync(string id)
    {
        var image = (await scenes.GetAsync(id))?.Image;
        if (!await scenes.DeleteAsync(id)) return false;
        images.Delete(image);
        return true;
    }

    public async Task<ActivationResult> ActivateSceneAsync(string id)
    {
        if (await scenes.GetAsync(id) is not { } scene)
            throw new ArgumentException($"Unknown scene '{id}'.");
        // SceneActivator (and the light provider it uses) is scoped, so activate inside a fresh scope.
        using var scope = scopeFactory.CreateScope();
        var activator = scope.ServiceProvider.GetRequiredService<SceneActivator>();
        return await activator.ActivateAsync(scene);
    }

    public ActiveSceneInfo GetActiveScene() => new(state.ActiveSceneId, state.ActivatedAt);

    // ---- Events ----

    public Task<List<GameEvent>> ListEventsAsync() => events.GetAllAsync();

    public Task<GameEvent?> GetEventAsync(string id) => events.GetAsync(id);

    // Mirrors PUT /events/{id}: stamp the id, validate, then upsert and drop replaced/cleared tile art.
    public async Task<GameEvent> UpsertEventAsync(GameEvent evt, string id)
    {
        evt.Id = id;
        EventValidation.Validate(evt);
        var oldImage = (await events.GetAsync(id))?.Image;
        await events.UpsertAsync(evt);
        if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, evt.Image, StringComparison.OrdinalIgnoreCase))
            images.Delete(oldImage);
        return evt;
    }

    // Mirrors DELETE /events/{id}: remove the tile art too. False when the event didn't exist.
    public async Task<bool> DeleteEventAsync(string id)
    {
        var image = (await events.GetAsync(id))?.Image;
        if (!await events.DeleteAsync(id)) return false;
        images.Delete(image);
        return true;
    }

    public async Task<EventResult> TriggerEventAsync(string id)
    {
        if (await events.GetAsync(id) is not { } evt)
            throw new ArgumentException($"Unknown event '{id}'.");
        // EventActivator (and the light provider it uses) is scoped, so trigger inside a fresh scope.
        using var scope = scopeFactory.CreateScope();
        var activator = scope.ServiceProvider.GetRequiredService<EventActivator>();
        return await activator.TriggerAsync(evt);
    }

    // Stop the running event timeline (if any). Returns whether one was running.
    public bool StopEvent() => timelineRunner.Stop();

    // ---- Light FX ----

    public Task<List<LightFx>> ListLightFxAsync() => lightFx.GetAllAsync();

    public Task<LightFx?> GetLightFxAsync(string id) => lightFx.GetAsync(id);

    // Mirrors PUT /lightfx/{id}: stamp the id, validate, upsert.
    public async Task<LightFx> UpsertLightFxAsync(LightFx fx, string id)
    {
        fx.Id = id;
        LightFxValidation.Validate(fx);
        await lightFx.UpsertAsync(fx);
        return fx;
    }

    // Mirrors DELETE /lightfx/{id}: detach every live reference into an embedded "custom" copy first, then
    // delete. False when the FX didn't exist.
    public async Task<bool> DeleteLightFxAsync(string id)
    {
        if (await lightFx.GetAsync(id) is not { } effect) return false;
        await LightFxDetacher.DetachReferencesAsync(effect, scenes, events);
        await lightFx.DeleteAsync(id);
        return true;
    }

    // Bounded preview of an FX on a light (null/empty key = the configured provider group), clamped 1–60 s.
    public async Task<LightFxTestInfo> TestLightFxAsync(string id, string? lightKey, int seconds = 10)
    {
        if (await lightFx.GetAsync(id) is not { } effect)
            throw new ArgumentException($"Unknown light FX '{id}'.");
        var window = Math.Clamp(seconds, 1, 60);
        // LightFxTester owns its own scope for the run; the base state is applied synchronously here so an
        // unconfigured/unreachable light throws instead of failing silently in the background loop.
        await fxTester.StartAsync(effect, string.IsNullOrWhiteSpace(lightKey) ? null : lightKey, window);
        return new LightFxTestInfo(effect.Id, window);
    }

    public bool StopLightFxTest() => fxTester.Stop();

    // ---- Context / control ----

    public IReadOnlyList<LightInfo> ListLights() =>
        lights.GetAll().Select(l => new LightInfo(l.Key, l.Name, l.Provider)).ToList();

    // Sound context only (no waveform backfill, unlike /sounds/list — this never mutates the store).
    public async Task<IReadOnlyList<SoundInfo>> ListSoundsAsync()
    {
        var all = await sounds.GetAllAsync();
        return all.Select(s => new SoundInfo(s.Id, s.Name, s.Category, s.Volume, s.Loop, s.DurationMs)).ToList();
    }

    public async Task<List<SpotifyPlaylist>> ListSpotifyPlaylistsAsync()
    {
        // SpotifyClient is a typed HttpClient (scoped) — resolve one per call, never cache it.
        using var scope = scopeFactory.CreateScope();
        var spotify = scope.ServiceProvider.GetRequiredService<SpotifyClient>();
        return await spotify.GetPlaylistsAsync();
    }

    public async Task<List<SpotifyTrack>> SearchSpotifyTracksAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Provide a search term.");
        using var scope = scopeFactory.CreateScope();
        var spotify = scope.ServiceProvider.GetRequiredService<SpotifyClient>();
        return await spotify.SearchTracksAsync(query);
    }

    // Mirrors GET|POST /lights/default — the panel's reset-lights button.
    public async Task ResetLightsAsync()
    {
        var def = settings.Current.DefaultLight
            ?? throw new ArgumentException("No default lighting is set — configure it on the Settings page first.");
        effects.StopAll();
        // ILightService is scoped (the provider is chosen per-scope from settings) — apply inside a fresh scope.
        using var scope = scopeFactory.CreateScope();
        var bulb = scope.ServiceProvider.GetRequiredService<ILightService>();
        await bulb.ApplyAsync(def);
    }
}
