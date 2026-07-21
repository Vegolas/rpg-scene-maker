using System.ComponentModel;
using ModelContextProtocol.Server;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services.Ai;

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

/// <summary>Music transport + playback state, across both sources (Spotify and the local file library).
/// Transport targets the active source (the one last played); play picks the source from the id shape.</summary>
[McpServerToolType]
public sealed class MusicMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "play_music"), Description(
        "Play music NOW: pass a spotify: URI or open.spotify.com link (from list_spotify_playlists / " +
        "search_spotify_tracks), OR a local library id — local:track:{id} or local:playlist:{id} (ids from a scene's " +
        "music or the library). The source is inferred from the id shape and becomes the active source. Throws if the " +
        "id is unrecognized, or (for Spotify) it isn't connected / no device is active.")]
    public Task<object> PlayMusic([Description("A spotify: URI / open.spotify.com link, or a local:track:{id} / local:playlist:{id} id.")] string uri) =>
        tools.PlayMusicAsync(uri);

    [McpServerTool(Name = "pause_music"), Description("Pause playback on the active music source (Spotify or local).")]
    public Task<object> PauseMusic() => tools.PauseMusicAsync();

    [McpServerTool(Name = "resume_music"), Description("Resume playback on the active music source (keeps the current queue/track).")]
    public Task<object> ResumeMusic() => tools.ResumeMusicAsync();

    [McpServerTool(Name = "next_track"), Description("Skip to the next track on the active music source.")]
    public Task<object> NextTrack() => tools.NextTrackAsync();

    [McpServerTool(Name = "previous_track"), Description("Go to the previous track on the active music source.")]
    public Task<object> PreviousTrack() => tools.PreviousTrackAsync();

    [McpServerTool(Name = "set_music_volume"), Description("Set the active music source's volume. value is 0.0 (mute) to 1.0 (full).")]
    public Task<object> SetMusicVolume([Description("Volume 0.0-1.0.")] double value) => tools.SetMusicVolumeAsync(value);

    [McpServerTool(Name = "set_music_shuffle"), Description("Turn shuffle on or off on the active music source.")]
    public Task<object> SetMusicShuffle([Description("true to shuffle, false to play in order.")] bool enabled) =>
        tools.SetMusicShuffleAsync(enabled);

    [McpServerTool(Name = "set_music_repeat"), Description("Set the active music source's repeat mode: off, track (repeat one), or playlist (repeat the whole context).")]
    public Task<object> SetMusicRepeat([Description("One of: off, track, playlist.")] string mode) =>
        tools.SetMusicRepeatAsync(mode);

    [McpServerTool(Name = "get_music_state"), Description("Get the current music playback state: the active source, the list of available sources, and track/artist, device, volume, progress, shuffle and repeat (isPlaying is false when nothing is playing).")]
    public Task<MusicStateDto> GetMusicState() => tools.GetMusicStateAsync();
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

/// <summary>The live table (issues #88/#120/#122): party players + table-level counters, plus the reusable
/// bestiary of enemy statblocks. Rendered on the key-free /tv display by a board's party/enemies element.</summary>
[McpServerToolType]
public sealed class PartyMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "list_party"), Description(
        "List the whole live table: players (each a name, optional portrait and generic counters), the table-level " +
        "counters (system-wide stats like Fear that belong to no single player), the bestiary enemies (reusable " +
        "statblocks), and system — the active game system's id (e.g. \"daggerheart\", null when none is chosen; it " +
        "drives the panel's presets and is set in Settings, not via tools). Counters are generic " +
        "{label, value, max, style, key} — key is an optional stable semantic id (lowercase slug, the preferred " +
        "adjust token) — the Daggerheart HP/Stress/Armor/Hope loadout is just a preset, not built in.")]
    public Task<PartyDto> ListParty() => tools.ListPartyAsync();

    [McpServerTool(Name = "upsert_player"), Description(
        "Create or replace the party player at the given id (the id arg wins; the body's own id is overwritten). " +
        "Rules: id is a lowercase slug [a-z0-9-_]; Name is required; optional Portrait is a stored image name (from an " +
        "/images upload); SortOrder sets roster order. Counters[] are generic {Label, Value, Max, Style}: Label is " +
        "unique within the player and doubles as the adjust key; Style is null|pips|number (pips needs a small Max ≤24); " +
        "up to 8 counters. Returns the stored player.")]
    public Task<PartyMember> UpsertPlayer(
        [Description("The full player entity to save (see the rules in this tool's description).")] PartyMember member,
        [Description("Player id to save it under: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.UpsertPlayerAsync(member, id);

    [McpServerTool(Name = "delete_player"), Description("Delete the party player with this id (and its portrait). Returns true if it existed, false otherwise.")]
    public Task<bool> DeletePlayer([Description("Player id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.DeletePlayerAsync(id);

    [McpServerTool(Name = "save_table_counters"), Description(
        "Replace the whole set of table-level counters (system-wide stats like Fear that belong to no single player). " +
        "Each counter is {Label, Value, Max, Style} with the same rules as a player's (unique label, pips needs a small " +
        "max, up to 8). Returns the stored list.")]
    public Task<List<PartyCounter>> SaveTableCounters(
        [Description("The full table-counter list to save.")] List<PartyCounter> counters) =>
        tools.SaveTableCountersAsync(counters);

    [McpServerTool(Name = "adjust_player_counter"), Description(
        "Adjust ONE of a player's counters live (e.g. mark 2 damage), clamped into [0, max]. Pass EXACTLY ONE of delta " +
        "(bump the current value, may be negative) or value (set it absolutely). counter is the counter's Label " +
        "(case-insensitive). Returns the updated player; errors if the player or counter is unknown.")]
    public Task<PartyMember> AdjustPlayerCounter(
        [Description("Player id (a lowercase slug [a-z0-9-_]).")] string id,
        [Description("The counter's semantic key (preferred, e.g. \"hp\") or its label — both case-insensitive.")] string counter,
        [Description("Bump the current value by this (may be negative). Give delta OR value, not both.")] int? delta = null,
        [Description("Set the value absolutely. Give value OR delta, not both.")] int? value = null) =>
        tools.AdjustPlayerCounterAsync(id, counter, delta, value);

    [McpServerTool(Name = "adjust_table_counter"), Description(
        "Adjust ONE table-level counter live (e.g. +1 Fear), clamped into [0, max]. Pass EXACTLY ONE of delta or value; " +
        "counter is the counter's Key or Label (case-insensitive). Returns the updated table-counter list; errors if no counter " +
        "matches the label.")]
    public Task<List<PartyCounter>> AdjustTableCounter(
        [Description("The counter's semantic key (preferred, e.g. \"fear\") or its label — both case-insensitive.")] string counter,
        [Description("Bump the current value by this (may be negative). Give delta OR value, not both.")] int? delta = null,
        [Description("Set the value absolutely. Give value OR delta, not both.")] int? value = null) =>
        tools.AdjustTableCounterAsync(counter, delta, value);

    [McpServerTool(Name = "upsert_enemy"), Description(
        "Create or replace a bestiary ENEMY STATBLOCK at the given id (the id arg wins). A statblock is a reusable " +
        "template — base stats only, NO live tracking (per-fight HP lives on an encounter's enemy instance). Rules: id " +
        "is a lowercase slug [a-z0-9-_]; Name required; optional Portrait (a stored image name); Counters[] are the base " +
        "definitions (same {Label, Value, Max, Style} rules as a player). Returns the stored statblock.")]
    public Task<Enemy> UpsertEnemy(
        [Description("The full enemy statblock to save (see the rules in this tool's description).")] Enemy enemy,
        [Description("Enemy id to save it under: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.UpsertEnemyAsync(enemy, id);

    [McpServerTool(Name = "delete_enemy"), Description("Delete the bestiary enemy statblock with this id (and its portrait). Returns true if it existed, false otherwise. Encounter instances already made from it are unaffected (they are snapshots).")]
    public Task<bool> DeleteEnemy([Description("Enemy id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.DeleteEnemyAsync(id);

    [McpServerTool(Name = "adjust_enemy_counter"), Description(
        "Adjust ONE of a bestiary enemy statblock's BASE counters, clamped into [0, max]. This edits the template's " +
        "starting values, NOT a live fight (use adjust_encounter_enemy for that). Pass EXACTLY ONE of delta or value; " +
        "counter is the Key or Label (case-insensitive). Returns the updated statblock; errors if the enemy or counter is unknown.")]
    public Task<Enemy> AdjustEnemyCounter(
        [Description("Enemy id (a lowercase slug [a-z0-9-_]).")] string id,
        [Description("The counter's semantic key (preferred, e.g. \"hp\") or its label — both case-insensitive.")] string counter,
        [Description("Bump the current value by this (may be negative). Give delta OR value, not both.")] int? delta = null,
        [Description("Set the value absolutely. Give value OR delta, not both.")] int? value = null) =>
        tools.AdjustEnemyCounterAsync(id, counter, delta, value);
}

/// <summary>Encounter CRUD + run + live enemy tracking (issue #122). An encounter is a prepped fight: chosen
/// heroes vs enemy instances over a background, optionally activating a scene/event, pushed to the /tv display.</summary>
[McpServerToolType]
public sealed class EncounterMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "list_encounters"), Description("List every saved encounter (full entities). An encounter is a prepped fight: HeroIds (party player ids; empty = all players), enemy instances (each a live copy of a bestiary statblock with its own counters and per-instance Spotlight/Hidden flags), an optional background image, and optional scene/event ids activated when it runs.")]
    public Task<List<Encounter>> ListEncounters() => tools.ListEncountersAsync();

    [McpServerTool(Name = "get_encounter"), Description("Get one encounter by id, or null if none has that id.")]
    public Task<Encounter?> GetEncounter([Description("Encounter id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.GetEncounterAsync(id);

    [McpServerTool(Name = "upsert_encounter"), Description(
        "Create or replace the encounter at the given id (the id arg wins). Rules: id is a lowercase slug [a-z0-9-_]; " +
        "Name required; HeroIds is a list of party player ids (empty means all current players); Enemies[] are the " +
        "instances — each needs a unique InstanceId, the source EnemyId (a bestiary statblock), a Name, its own " +
        "Counters[] (seeded from the statblock), and optional Spotlight (boss highlight) / Hidden (held back) flags; " +
        "optional BackgroundImage (a stored image name); optional ActivateSceneId / ActivateEventId (slugs, run " +
        "best-effort on run). Returns the stored encounter.")]
    public Task<Encounter> UpsertEncounter(
        [Description("The full encounter entity to save (see the rules in this tool's description).")] Encounter encounter,
        [Description("Encounter id to save it under: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.UpsertEncounterAsync(encounter, id);

    [McpServerTool(Name = "delete_encounter"), Description("Delete the encounter with this id (and its owned background image; enemy-instance portraits are bestiary snapshots and are left alone). Clears the /tv display if this encounter was showing. Returns true if it existed, false otherwise.")]
    public Task<bool> DeleteEncounter([Description("Encounter id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.DeleteEncounterAsync(id);

    [McpServerTool(Name = "run_encounter"), Description(
        "Run the encounter NOW: activate its configured scene and event best-effort (each reports " +
        "ok/skipped/notFound/partial/error but never blocks the show) and push its heroes-left / enemies-right view to " +
        "the /tv display. Returns { rev, encounter, scene, event }. Throws if the encounter id is unknown.")]
    public Task<object> RunEncounter([Description("Encounter id to run: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.RunEncounterAsync(id);

    [McpServerTool(Name = "adjust_encounter_enemy"), Description(
        "Adjust ONE counter of ONE enemy instance in a prepped/running fight (e.g. mark 3 damage on the boss), clamped " +
        "into [0, max]. Pass EXACTLY ONE of delta or value; counter is the counter's Key or Label (case-insensitive). Returns " +
        "the updated encounter; errors if the encounter, instance or counter is unknown.")]
    public Task<Encounter> AdjustEncounterEnemy(
        [Description("Encounter id (a lowercase slug [a-z0-9-_]).")] string id,
        [Description("The enemy instance's InstanceId within that encounter.")] string instanceId,
        [Description("The counter's semantic key (preferred, e.g. \"hp\") or its label — both case-insensitive.")] string counter,
        [Description("Bump the current value by this (may be negative). Give delta OR value, not both.")] int? delta = null,
        [Description("Set the value absolutely. Give value OR delta, not both.")] int? value = null) =>
        tools.AdjustEncounterEnemyAsync(id, instanceId, counter, delta, value);

    [McpServerTool(Name = "reset_encounter"), Description("Reset every enemy instance's counters to its statblock's starting values (before re-running the same fight). Returns the updated encounter. Throws if the encounter id is unknown.")]
    public Task<Encounter> ResetEncounter([Description("Encounter id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.ResetEncounterAsync(id);
}

/// <summary>Board CRUD (issues #80/#88). A board is composable player-facing TV content — a 16:9 layout of
/// positioned image/text/party/enemies elements — that the GM pushes to the /tv display.</summary>
[McpServerToolType]
public sealed class BoardMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "list_boards"), Description("List every saved board (full entities). A board is a 16:9 player-facing TV layout: a background colour or image plus positioned elements (image/text or a live party/enemies roster), all in percent-of-stage coordinates.")]
    public Task<List<Board>> ListBoards() => tools.ListBoardsAsync();

    [McpServerTool(Name = "get_board"), Description("Get one board by id, or null if none has that id.")]
    public Task<Board?> GetBoard([Description("Board id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.GetBoardAsync(id);

    [McpServerTool(Name = "upsert_board"), Description(
        "Create or replace the board at the given id (the id arg wins). Rules: id is a lowercase slug [a-z0-9-_]; Name " +
        "required; optional BackgroundColor (#RRGGBB) and/or BackgroundImage (a stored image name); up to 50 Elements[]. " +
        "Each element has a Kind (image|text|party|enemies) and percent-of-stage geometry X/Y (0–100) and W/H (0.1–100); " +
        "the list order IS the z-order (index 0 is at the bottom). An image element needs Image (a stored image name); a " +
        "text element needs Text (+ optional Color/Size/Align); party/enemies elements are geometry-only live " +
        "placeholders that render the roster at display time. Returns the stored board.")]
    public Task<Board> UpsertBoard(
        [Description("The full board entity to save (see the rules in this tool's description).")] Board board,
        [Description("Board id to save it under: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.UpsertBoardAsync(board, id);

    [McpServerTool(Name = "delete_board"), Description("Delete the board with this id (and its owned images). Clears the /tv display if this board was showing. Returns true if it existed, false otherwise.")]
    public Task<bool> DeleteBoard([Description("Board id: a lowercase slug [a-z0-9-_].")] string id) =>
        tools.DeleteBoardAsync(id);
}

/// <summary>The player-facing /tv display (issues #80/#122): push a prepared image, a board, or an encounter to
/// the shared table screen, clear it, or read what's currently shown.</summary>
[McpServerToolType]
public sealed class TvMcpTools(AiToolService tools)
{
    [McpServerTool(Name = "show_on_tv"), Description(
        "Push content to the player-facing /tv display. Pass EXACTLY ONE of: image (a stored image name from an /images " +
        "upload — a handout/map), board (a board id from list_boards), or encounter (an encounter id from " +
        "list_encounters — shows its heroes-left / enemies-right view). Optional label overrides the caption (defaults " +
        "to the board's/encounter's name). Returns the new display rev. Errors if none / more than one target is given, " +
        "or the target doesn't exist.")]
    public Task<object> ShowOnTv(
        [Description("A stored image file name to show; omit unless showing an image.")] string? image = null,
        [Description("A board id to show; omit unless showing a board.")] string? board = null,
        [Description("An encounter id to show; omit unless showing an encounter.")] string? encounter = null,
        [Description("Optional caption; defaults to the board's/encounter's name.")] string? label = null) =>
        tools.ShowOnTvAsync(image, board, encounter, label);

    [McpServerTool(Name = "clear_tv"), Description("Clear the player-facing /tv display (nothing shown). Returns the new display rev.")]
    public object ClearTv() => tools.ClearTv();

    [McpServerTool(Name = "get_tv_state"), Description("Get what the /tv display is currently showing: the live rev plus the pushed content's kind (image|board|encounter), its ref (image name or entity id) and label — all null when the display is cleared.")]
    public TvStatusInfo GetTvState() => tools.GetTvState();
}
