using System.ComponentModel;
using ModelContextProtocol.Server;
using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services.Ai;

// MCP tool surface: thin [McpServerTool] adapters over the shared AiToolService singleton, grouped into
// tool-type classes by domain (scenes / events / screens / light FX / music / sounds / library+control) so
// the MCP server (hosted at /mcp, wired in Program.cs) and the in-panel assistant call the exact same facade.
// Each method returns the facade's result (entity/record) directly — the SDK serializes it with camelCase Web
// defaults, matching the HTTP wire. Facade validation throws ArgumentException (bad slug/reserved id/unknown
// ref); the MCP SDK turns a thrown exception into a tool-call error the model can read and correct, so we let
// them propagate. The descriptions carry the domain rules an LLM needs to build valid entities without seeing
// the HTTP endpoints.

/// <summary>Scene CRUD + live control. A scene is a whole table state (lighting + music + one-shot sounds).</summary>
[McpServerToolType]
public sealed class SceneMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "list_scenes"), Description("List every saved scene (full entities). A scene bundles a table mood: per-light control, optional music, and one-shot sound effects.")]
    public Task<List<Scene>> ListScenes() => tools.ListScenesAsync();

    [McpServerTool(Name = "get_scene"), Description("Get one scene by id, or null if no scene has that id.")]
    public Task<Scene?> GetScene([Description("Scene id: a lowercase slug matching [a-z0-9-_].")] string id) =>
        tools.GetSceneAsync(id);

    [McpServerTool(Name = "upsert_scene"), Description(
        "Create or replace the scene at the given id (the id in the URL wins; the scene body's own id is overwritten). " +
        "Rules the scene body must follow: id is a lowercase slug [a-z0-9-_]. " +
        "Lighting: prefer per-light control via Lights[] — each SceneLight has a LightKey that MUST match a registered light key from list_lights, plus power/color/white; colors are #RRGGBB hex, Brightness and Temperature are 0–100. " +
        "A SceneLight may carry an Effect (LightEffect) whose Type is one of flicker|glow|storm|drift|custom|fx; Type 'fx' additionally requires FxId referencing a library FX from list_light_fx. " +
        "The legacy single Light block still works for simple all-lights scenes. " +
        "Music (MusicSettings): PlayId is a spotify: URI or an open.spotify.com link (find them with list_spotify_playlists / search_spotify_tracks); Volume is 0.0–1.0. " +
        "SoundEffects holds sound ids from list_sounds. Returns the stored scene.")]
    public Task<Scene> UpsertScene(
        [Description("The full scene entity to save (see the rules in this tool's description).")] Scene scene,
        [Description("Scene id to save it under: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.UpsertSceneAsync(scene, id);

    [McpServerTool(Name = "delete_scene"), Description("Delete the scene with this id (and its tile image). Returns true if it existed, false otherwise.")]
    public Task<bool> DeleteScene([Description("Scene id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.DeleteSceneAsync(id);

    [McpServerTool(Name = "activate_scene"), Description(
        "Activate a scene NOW on the real table: apply its lights, music and sounds concurrently. Returns per-part " +
        "ok/skipped/error statuses (e.g. lights may 'error' with no bulb reachable while music still 'ok'). Throws if the scene id is unknown.")]
    public Task<ActivationResult> ActivateScene([Description("Scene id to activate: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.ActivateSceneAsync(id);

    [McpServerTool(Name = "get_active_scene"), Description("Get the scene currently showing on the table (its id and when it was activated; id is null if none has been activated yet).")]
    public ActiveSceneInfo GetActiveScene() => tools.GetActiveScene();
}

/// <summary>Event CRUD + live control. An event is a one-shot triggered effect (a light flash and/or overlaid sounds, optionally a timeline).</summary>
[McpServerToolType]
public sealed class EventMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "list_events"), Description("List every saved event (full entities). An event is a one-shot effect: a brief light flash and/or sounds that overlay current playback, optionally an ms-based timeline of clips.")]
    public Task<List<GameEvent>> ListEvents() => tools.ListEventsAsync();

    [McpServerTool(Name = "get_event"), Description("Get one event by id, or null if no event has that id.")]
    public Task<GameEvent?> GetEvent([Description("Event id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.GetEventAsync(id);

    [McpServerTool(Name = "upsert_event"), Description(
        "Create or replace the event at the given id (the id in the URL wins). Rules: id is a lowercase slug [a-z0-9-_] and must NOT be one of the reserved ids list, stop, state. " +
        "Flash (optional): a colour to jump to (#RRGGBB hex, Brightness/Temperature 0–100) held for DurationMs, then the live scene's lights are restored. " +
        "SoundEffects holds sound ids from list_sounds and overlay current playback (no stop-all). " +
        "Timeline (optional): sound and light clips placed at millisecond offsets with durations; light clips use the same LightEffect Type rules as scenes (flicker|glow|storm|drift|custom|fx, fx needs FxId). Returns the stored event.")]
    public Task<GameEvent> UpsertEvent(
        [Description("The full event entity to save (see the rules in this tool's description).")] GameEvent evt,
        [Description("Event id to save it under: a lowercase slug [a-z0-9-_], not the reserved list/stop/state.")] string id) =>
        tools.UpsertEventAsync(evt, id);

    [McpServerTool(Name = "delete_event"), Description("Delete the event with this id (and its tile image). Returns true if it existed, false otherwise.")]
    public Task<bool> DeleteEvent([Description("Event id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.DeleteEventAsync(id);

    [McpServerTool(Name = "trigger_event"), Description(
        "Fire an event NOW: its flash and overlaid sounds run concurrently (each reports ok/skipped/error), or, if it has a non-empty timeline, the timeline starts in the background and returns immediately. Throws if the event id is unknown.")]
    public Task<EventResult> TriggerEvent([Description("Event id to trigger: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.TriggerEventAsync(id);

    [McpServerTool(Name = "stop_event"), Description("Stop the currently running event timeline, if any. Returns true if one was running.")]
    public bool StopEvent() => tools.StopEvent();

    [McpServerTool(Name = "get_event_state"), Description("Get the id of the event whose timeline is currently running, or null if none is running.")]
    public EventStateDto GetEventState() => tools.GetEventState();
}

/// <summary>Screen CRUD. A screen is an organizational board of shortcut tiles onto existing scenes/events/sounds/music.</summary>
[McpServerToolType]
public sealed class ScreenMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "list_screens"), Description("List every saved screen (full entities). A screen is a board of shortcut tiles pointing at existing scenes/events/sounds/music/reset-lights — purely organizational, it owns no state of its own.")]
    public Task<List<Screen>> ListScreens() => tools.ListScreensAsync();

    [McpServerTool(Name = "get_screen"), Description("Get one screen by id, or null if no screen has that id.")]
    public Task<Screen?> GetScreen([Description("Screen id: a lowercase slug matching [a-z0-9-_].")] string id) =>
        tools.GetScreenAsync(id);

    [McpServerTool(Name = "upsert_screen"), Description(
        "Create or replace the screen at the given id (the id in the URL wins; the screen body's own id is overwritten). " +
        "Rules the screen body must follow: id is a lowercase slug [a-z0-9-_]; Name is required; up to 100 Tiles. " +
        "Each ScreenTile has a Kind of scene|event|sound|music|light-reset|break plus a Ref and Label: for scene/event/sound the Ref is that entity's id (from list_scenes/list_events/list_sounds); " +
        "for music the Ref is a spotify: URI or open.spotify.com link and a Label is required; light-reset and break take no Ref (break is a layout-only line break whose Label is an optional section heading). " +
        "A screen references existing entities only — it never carries light/music/sound state. Returns the stored screen.")]
    public Task<Screen> UpsertScreen(
        [Description("The full screen entity to save (see the rules in this tool's description).")] Screen screen,
        [Description("Screen id to save it under: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.UpsertScreenAsync(screen, id);

    [McpServerTool(Name = "delete_screen"), Description("Delete the screen with this id (and its tile image). Returns true if it existed, false otherwise. Nothing references a screen, so no other entities are affected.")]
    public Task<bool> DeleteScreen([Description("Screen id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.DeleteScreenAsync(id);
}

/// <summary>Light FX library CRUD + live preview. A Light FX is a reusable named keyframe animation referenced by scenes and event timelines.</summary>
[McpServerToolType]
public sealed class LightFxMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "list_light_fx"), Description("List every saved Light FX (full entities). A Light FX is a reusable named keyframe animation that scene lights and event timeline clips reference by id (via LightEffect Type 'fx' + FxId).")]
    public Task<List<LightFx>> ListLightFx() => tools.ListLightFxAsync();

    [McpServerTool(Name = "get_light_fx"), Description("Get one Light FX by id, or null if none has that id.")]
    public Task<LightFx?> GetLightFx([Description("Light FX id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.GetLightFxAsync(id);

    [McpServerTool(Name = "upsert_light_fx"), Description(
        "Create or replace the Light FX at the given id (the id in the URL wins). Rules: id is a lowercase slug [a-z0-9-_] and must NOT be one of the reserved ids list, test, stop. " +
        "The FX is a Keyframes sequence (same shape as a 'custom' LightEffect): each keyframe has a colour (#RRGGBB hex, Brightness/Temperature 0–100) and timing. Returns the stored FX.")]
    public Task<LightFx> UpsertLightFx(
        [Description("The full Light FX entity to save (see the rules in this tool's description).")] LightFx fx,
        [Description("Light FX id to save it under: a lowercase slug [a-z0-9-_], not the reserved list/test/stop.")] string id) =>
        tools.UpsertLightFxAsync(fx, id);

    [McpServerTool(Name = "delete_light_fx"), Description(
        "Delete the Light FX with this id. Every scene light / timeline clip that referenced it is first detached — rewritten in place to embed a 'custom' copy of the keyframes — so nothing dangles. Returns true if it existed, false otherwise.")]
    public Task<bool> DeleteLightFx([Description("Light FX id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.DeleteLightFxAsync(id);

    [McpServerTool(Name = "test_light_fx"), Description(
        "Preview a Light FX NOW on a real light for a bounded window, then restore the live lights. Throws if the FX id is unknown or the target light is unreachable.")]
    public Task<LightFxTestInfo> TestLightFx(
        [Description("Light FX id to preview: a lowercase slug [a-z0-9-_].")] string id,
        [Description("Optional light key (from list_lights) to preview on; null/empty previews on the configured provider group.")] string? lightKey = null,
        [Description("Preview window in seconds, clamped to 1–60. Defaults to 10.")] int seconds = 10) =>
        tools.TestLightFxAsync(id, lightKey, seconds);

    [McpServerTool(Name = "stop_light_fx_test"), Description("Stop the running Light FX preview, if any, and restore the live lights. Returns true if a preview was running.")]
    public bool StopLightFxTest() => tools.StopLightFxTest();
}

/// <summary>Read-only context (lights, sounds, Spotify) plus the reset-lights control.</summary>
[McpServerToolType]
public sealed class LibraryMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "list_lights"), Description("List the registered lights (key, name, provider). Use each light's key as a SceneLight.LightKey or as test_light_fx's lightKey.")]
    public IReadOnlyList<LightInfo> ListLights() => tools.ListLights();

    [McpServerTool(Name = "list_sounds"), Description("List the soundboard sounds (id, name, category, volume, loop, duration). Use each sound's id in a scene's or event's SoundEffects.")]
    public Task<IReadOnlyList<SoundInfo>> ListSounds() => tools.ListSoundsAsync();

    [McpServerTool(Name = "list_spotify_playlists"), Description("List the connected Spotify account's playlists. Each has a spotify: URI usable as MusicSettings.PlayId. Requires Spotify to be connected.")]
    public Task<List<SpotifyPlaylist>> ListSpotifyPlaylists() => tools.ListSpotifyPlaylistsAsync();

    [McpServerTool(Name = "search_spotify_tracks"), Description("Search Spotify for tracks matching a query. Each result has a spotify: URI usable as MusicSettings.PlayId. Requires Spotify to be connected.")]
    public Task<List<SpotifyTrack>> SearchSpotifyTracks([Description("Search terms, e.g. a song title and/or artist.")] string query) =>
        tools.SearchSpotifyTracksAsync(query);

    [McpServerTool(Name = "reset_lights"), Description("Reset all lights to the configured default lighting state (the panel's reset-lights button). Throws if no default lighting has been set on the Settings page.")]
    public Task ResetLights() => tools.ResetLightsAsync();

    [McpServerTool(Name = "get_lights_status"), Description("Get the live bulb state: normalized on/off, mode (colour/white), brightness, colour (RRGGBB hex) and white temperature, plus the raw Tuya/Hue payload for diagnostics. Throws if the light is unreachable.")]
    public Task<LightStatus> GetLightsStatus() => tools.GetLightsStatusAsync();
}

/// <summary>Spotify music transport + playback state. Requires Spotify to be connected (Settings) with an active device.</summary>
[McpServerToolType]
public sealed class MusicMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "play_music"), Description(
        "Play music NOW on the connected Spotify device: pass a spotify: URI or an open.spotify.com link " +
        "(from list_spotify_playlists / search_spotify_tracks). Throws if the link is invalid, Spotify isn't connected, or no device is active.")]
    public Task<object> PlayMusic([Description("A spotify: URI or open.spotify.com link (track/playlist/album/artist).")] string uri) =>
        tools.PlayMusicAsync(uri);

    [McpServerTool(Name = "pause_music"), Description("Pause Spotify playback on the connected device.")]
    public Task<object> PauseMusic() => tools.PauseMusicAsync();

    [McpServerTool(Name = "resume_music"), Description("Resume Spotify playback on the connected device (keeps the current queue/track).")]
    public Task<object> ResumeMusic() => tools.ResumeMusicAsync();

    [McpServerTool(Name = "next_track"), Description("Skip to the next Spotify track.")]
    public Task<object> NextTrack() => tools.NextTrackAsync();

    [McpServerTool(Name = "previous_track"), Description("Go to the previous Spotify track.")]
    public Task<object> PreviousTrack() => tools.PreviousTrackAsync();

    [McpServerTool(Name = "set_music_volume"), Description("Set the Spotify device volume. value is 0.0 (mute) to 1.0 (full).")]
    public Task<object> SetMusicVolume([Description("Volume 0.0-1.0.")] double value) => tools.SetMusicVolumeAsync(value);

    [McpServerTool(Name = "set_music_shuffle"), Description("Turn Spotify shuffle on or off.")]
    public Task<object> SetMusicShuffle([Description("true to shuffle, false to play in order.")] bool enabled) =>
        tools.SetMusicShuffleAsync(enabled);

    [McpServerTool(Name = "set_music_repeat"), Description("Set the Spotify repeat mode: off, track (repeat one), or playlist (repeat the whole context).")]
    public Task<object> SetMusicRepeat([Description("One of: off, track, playlist.")] string mode) =>
        tools.SetMusicRepeatAsync(mode);

    [McpServerTool(Name = "get_music_state"), Description("Get the current Spotify playback state (track/artist, device, volume, progress, shuffle, repeat), or null if nothing is active.")]
    public Task<SpotifyPlaybackState?> GetMusicState() => tools.GetMusicStateAsync();
}

/// <summary>Soundboard live control + metadata. Sounds play on the server's own audio device (overlapping voices).</summary>
[McpServerToolType]
public sealed class SoundMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "play_sound"), Description(
        "Play one soundboard sound NOW on the server's speakers, overlaying anything already playing (find ids with list_sounds). " +
        "Optional volume 0.0-1.0 overrides the sound's stored level. Throws if the id is unknown or the audio device/file is unavailable.")]
    public Task<object> PlaySound(
        [Description("Sound id (from list_sounds).")] string id,
        [Description("Optional playback volume 0.0-1.0; omit to use the sound's stored volume.")] double? volume = null) =>
        tools.PlaySoundAsync(id, volume);

    [McpServerTool(Name = "stop_sound"), Description("Stop every voice currently playing this sound id (a no-op if it isn't playing).")]
    public object StopSound([Description("Sound id (from list_sounds).")] string id) => tools.StopSound(id);

    [McpServerTool(Name = "stop_all_sounds"), Description("Stop all soundboard playback on the server.")]
    public object StopAllSounds() => tools.StopAllSounds();

    [McpServerTool(Name = "update_sound"), Description(
        "Update a soundboard sound's editable metadata (name/category/volume/loop). Every field is optional; each omitted field is left unchanged. " +
        "Does not touch the audio file or the tile art. Returns the updated sound.")]
    public Task<SoundInfo> UpdateSound(
        [Description("Sound id (from list_sounds).")] string id,
        [Description("New display name; omit to leave unchanged.")] string? name = null,
        [Description("New category; omit to leave unchanged.")] string? category = null,
        [Description("New default volume 0.0-1.0; omit to leave unchanged.")] double? volume = null,
        [Description("Whether the sound loops; omit to leave unchanged.")] bool? loop = null) =>
        tools.UpdateSoundAsync(id, name, category, volume, loop);

    [McpServerTool(Name = "get_sounds_state"), Description("Get the ids of the sounds currently playing on the server's soundboard.")]
    public SoundStateDto GetSoundsState() => tools.GetSoundsState();
}
