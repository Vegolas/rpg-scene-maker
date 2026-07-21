using System.Net.Sockets;
using System.Text.Json;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Ai.Providers;

namespace AmbientDirector.Api.Services.Ai;

/// <summary>
/// The in-panel assistant's tool surface: the same façade operations the MCP server exposes, but as
/// provider-neutral <see cref="AiToolDefinition"/>s plus an executor. <see cref="Definitions"/> is the
/// static schema list sent to the model on every turn (each provider adapter maps it to its own SDK's tool
/// type); <see cref="ExecuteAsync"/> dispatches a tool call back onto <see cref="AiToolService"/>.
/// Integration/validation failures are caught here and returned as error tool-results (IsError=true) so the
/// model can read the message and self-correct instead of the run crashing. Tool names, param names and
/// entity JSON shapes match the MCP tools byte-for-byte (camelCase via <see cref="AiJson"/>), so both
/// surfaces behave identically — the AiToolSurfaceParityTests guard that the two name sets stay equal.
/// </summary>
public sealed class AssistantTools(AiToolService tools)
{
    // ---- Tool schemas (sent to the model every turn) ----

    private const string SceneShape =
        "The full scene entity as camelCase JSON (same shape the HTTP API accepts). Fields: id (a lowercase " +
        "slug [a-z0-9-_]; the id arg wins regardless), name, optional image. Lighting: prefer per-light " +
        "control via lights[] — each entry has lightKey (MUST match a registered key from list_lights), power " +
        "(bool) and color/white settings; colors are #RRGGBB hex, brightness and temperature are 0-100. A " +
        "light entry may carry an effect object whose type is one of flicker|glow|storm|drift|custom|fx; type " +
        "'fx' also needs fxId referencing a library FX from list_light_fx, and 'custom' carries keyframes[] " +
        "(each {atMs, color, brightness, temperature, transitionMs}). A legacy single light block still works " +
        "for simple all-lights scenes. music (optional MusicSettings): playId is a spotify: URI or " +
        "open.spotify.com link (find via list_spotify_playlists / search_spotify_tracks), volume is 0.0-1.0, " +
        "pause is a bool. soundEffects is an array of sound ids from list_sounds fired on activation.";

    private const string EventShape =
        "The full event entity as camelCase JSON. Fields: id (a lowercase slug [a-z0-9-_] and NOT one of the " +
        "reserved ids list, stop, state; the id arg wins), name, optional image. flash (optional): a color to " +
        "jump to (#RRGGBB hex, brightness/temperature 0-100) held for durationMs, then the live scene's lights " +
        "are restored. soundEffects is an array of sound ids from list_sounds that overlay current playback " +
        "(no stop-all). timeline (optional): sound and light clips placed at millisecond offsets with " +
        "durations; light clips use the same effect type rules as scene lights (flicker|glow|storm|drift|" +
        "custom|fx; fx needs fxId, custom needs keyframes[]).";

    private const string FxShape =
        "The full Light FX entity as camelCase JSON. Fields: id (a lowercase slug [a-z0-9-_] and NOT one of " +
        "the reserved ids list, test, stop; the id arg wins), name, and keyframes[] — the animation (same " +
        "shape as a 'custom' LightEffect): each keyframe has a color (#RRGGBB hex), brightness and temperature " +
        "(0-100), atMs (offset) and transitionMs (ramp duration to that keyframe).";

    private const string ScreenShape =
        "The full screen entity as camelCase JSON. Fields: id (a lowercase slug [a-z0-9-_]; the id arg wins), " +
        "name (required), optional image, compact (bool display hint), and tiles[] (up to 100). Each tile has " +
        "kind (one of scene|event|sound|music|light-reset|break) plus ref and label: for scene/event/sound the " +
        "ref is that entity's id (from list_scenes/list_events/list_sounds); for music the ref is a spotify: " +
        "URI or open.spotify.com link and label is required; light-reset and break take no ref (break is a " +
        "layout line break whose label is an optional section heading). A screen owns no light/music/sound " +
        "state — it only references existing entities.";

    private const string PlayerShape =
        "The full party player entity as camelCase JSON. Fields: id (a lowercase slug [a-z0-9-_]; the id arg " +
        "wins), name (required), optional portrait (a stored image file name from an /images upload), sortOrder " +
        "(int roster order), and counters[] — generic trackers, each {label, value, max, style, key}: label is " +
        "unique within the player and a case-insensitive adjust token; key is an optional stable semantic id " +
        "(lowercase slug, e.g. \"hp\" — the preferred adjust token, stamped by the active game system's presets; " +
        "keep an existing counter's key when editing); max is null (unbounded) or 1-999; style is " +
        "null|\"pips\"|\"number\" (\"pips\" needs a small max ≤24); up to 8 counters. The Daggerheart " +
        "HP/Stress/Armor/Hope loadout is just a preset, not built in.";

    private const string EnemyShape =
        "The full bestiary enemy STATBLOCK as camelCase JSON — a reusable template, base stats only (no live " +
        "tracking; per-fight HP lives on an encounter's enemy instance). Fields: id (a lowercase slug [a-z0-9-_]; " +
        "the id arg wins), name (required), optional portrait (a stored image file name), sortOrder (int), and " +
        "counters[] the base definitions ({label, value, max, style}, same rules as a player's).";

    private const string CountersShape =
        "An array of table-level counters, each a generic tracker {label, value, max, style, key}: label unique " +
        "(a case-insensitive adjust token), key an optional stable semantic id (lowercase slug, e.g. \"fear\" — " +
        "the preferred adjust token; keep existing keys when editing), max null or 1-999, style " +
        "null|\"pips\"|\"number\" (\"pips\" needs a small max ≤24); up to 8. These are system-wide stats (like " +
        "Fear) that belong to no single player.";

    private const string EncounterShape =
        "The full encounter entity as camelCase JSON. Fields: id (a lowercase slug [a-z0-9-_]; the id arg wins), " +
        "name (required), sortOrder (int), heroIds[] (party player ids — empty means all current players), " +
        "enemies[] the instances (each {instanceId (unique within the encounter), enemyId (the source bestiary " +
        "statblock id), name, optional portrait, spotlight (bool boss highlight), hidden (bool held back), " +
        "counters[] its own live {label,value,max,style} trackers seeded from the statblock}), optional " +
        "backgroundImage (a stored image name), and optional activateSceneId / activateEventId (slugs run " +
        "best-effort when the encounter runs).";

    private const string BoardShape =
        "The full board entity as camelCase JSON. Fields: id (a lowercase slug [a-z0-9-_]; the id arg wins), " +
        "name (required), optional backgroundColor (#RRGGBB) and/or backgroundImage (a stored image name), and " +
        "elements[] (up to 50). Each element has kind (image|text|party|enemies) and percent-of-stage geometry " +
        "x/y (0-100), w/h (0.1-100); the list order IS the z-order (index 0 draws at the bottom). An image " +
        "element needs image (a stored image name); a text element needs text (+ optional color #RRGGBB, size " +
        "1-100, align left|center|right); party and enemies elements are geometry-only live placeholders that " +
        "render the roster at display time (no content fields).";

    /// <summary>The tool definitions, matching the MCP tool names and the façade operations 1:1.</summary>
    public static IReadOnlyList<AiToolDefinition> Definitions { get; } =
    [
        // Scenes
        NoArgs("list_scenes",
            "List every saved scene (full entities). A scene bundles a table mood: per-light control, optional music, and one-shot sound effects."),
        WithId("get_scene", "Get one scene by id, or null if none has that id."),
        Tool2("upsert_scene",
            "Create or replace the scene at the given id (the id arg wins; the scene body's own id is overwritten). Returns the stored scene.",
            "id", "Scene id to save under: a lowercase slug [a-z0-9-_].",
            "scene", SceneShape),
        WithId("delete_scene", "Delete the scene with this id (and its tile image). Returns true if it existed, false otherwise."),
        WithId("activate_scene",
            "Activate a scene NOW on the real table: apply its lights, music and sounds concurrently. Returns per-part ok/skipped/error statuses (lights may 'error' with no bulb reachable while music is still 'ok'). Errors if the scene id is unknown."),
        NoArgs("get_active_scene",
            "Get the scene currently showing on the table (its id and when it was activated; id is null if none has been activated yet)."),

        // Events
        NoArgs("list_events",
            "List every saved event (full entities). An event is a one-shot effect: a brief light flash and/or sounds that overlay current playback, optionally an ms-based timeline of clips."),
        WithId("get_event", "Get one event by id, or null if none has that id."),
        Tool2("upsert_event",
            "Create or replace the event at the given id (the id arg wins). Returns the stored event.",
            "id", "Event id to save under: a lowercase slug [a-z0-9-_], not the reserved list/stop/state.",
            "evt", EventShape),
        WithId("delete_event", "Delete the event with this id (and its tile image). Returns true if it existed, false otherwise."),
        WithId("trigger_event",
            "Fire an event NOW: its flash and overlaid sounds run concurrently (each reports ok/skipped/error), or, if it has a non-empty timeline, the timeline starts in the background and returns immediately. Errors if the event id is unknown."),
        NoArgs("stop_event", "Stop the currently running event timeline, if any. Returns true if one was running."),
        NoArgs("get_event_state", "Get the id of the event whose timeline is currently running, or null if none is running."),

        // Light FX
        NoArgs("list_light_fx",
            "List every saved Light FX (full entities). A Light FX is a reusable named keyframe animation that scene lights and event timeline clips reference by id (via a light effect of type 'fx' + fxId)."),
        WithId("get_light_fx", "Get one Light FX by id, or null if none has that id."),
        Tool2("upsert_light_fx",
            "Create or replace the Light FX at the given id (the id arg wins). Returns the stored FX.",
            "id", "Light FX id to save under: a lowercase slug [a-z0-9-_], not the reserved list/test/stop.",
            "fx", FxShape),
        WithId("delete_light_fx",
            "Delete the Light FX with this id. Every scene light / timeline clip that referenced it is first detached — rewritten in place to embed a 'custom' copy of the keyframes — so nothing dangles. Returns true if it existed, false otherwise."),
        new AiToolDefinition(
            "test_light_fx",
            "Preview a Light FX NOW on a real light for a bounded window, then restore the live lights. Errors if the FX id is unknown or the target light is unreachable.",
            Schema(
                new()
                {
                    ["id"] = Prop("string", "Light FX id to preview: a lowercase slug [a-z0-9-_]."),
                    ["lightKey"] = Prop("string", "Optional light key (from list_lights) to preview on; omit/empty previews on the configured provider group."),
                    ["seconds"] = Prop("integer", "Preview window in seconds, clamped to 1-60. Defaults to 10."),
                },
                required: ["id"])),
        NoArgs("stop_light_fx_test", "Stop the running Light FX preview, if any, and restore the live lights. Returns true if a preview was running."),

        // Screens
        NoArgs("list_screens", "List every saved screen (full entities). A screen is a board of shortcut tiles pointing at existing scenes/events/sounds/music/reset-lights — purely organizational, it owns no state of its own."),
        WithId("get_screen", "Get one screen by id, or null if none has that id."),
        Tool2("upsert_screen",
            "Create or replace the screen at the given id (the id arg wins). Returns the stored screen.",
            "id", "Screen id to save under: a lowercase slug [a-z0-9-_].",
            "screen", ScreenShape),
        WithId("delete_screen", "Delete the screen with this id (and its tile image). Returns true if it existed, false otherwise. Nothing references a screen, so no other entities are affected."),

        // Music (source-routed transport: Spotify or the local file library)
        new AiToolDefinition(
            "play_music",
            "Play music NOW: a spotify: URI or open.spotify.com link (from list_spotify_playlists / search_spotify_tracks), OR a local library id — local:track:{id} or local:playlist:{id}. The source is inferred from the id shape and becomes the active source. Errors if the id is unrecognized, or (for Spotify) it isn't connected / no device is active.",
            Schema(
                new() { ["uri"] = Prop("string", "A spotify: URI / open.spotify.com link, or a local:track:{id} / local:playlist:{id} id.") },
                required: ["uri"])),
        NoArgs("pause_music", "Pause playback on the active music source (Spotify or local)."),
        NoArgs("resume_music", "Resume playback on the active music source (keeps the current queue/track)."),
        NoArgs("next_track", "Skip to the next track on the active music source."),
        NoArgs("previous_track", "Go to the previous track on the active music source."),
        new AiToolDefinition(
            "set_music_volume",
            "Set the active music source's volume (0.0 mute – 1.0 full).",
            Schema(
                new() { ["value"] = Prop("number", "Volume 0.0-1.0.") },
                required: ["value"])),
        new AiToolDefinition(
            "set_music_shuffle",
            "Turn shuffle on or off on the active music source.",
            Schema(
                new() { ["enabled"] = Prop("boolean", "true to shuffle, false to play in order.") },
                required: ["enabled"])),
        new AiToolDefinition(
            "set_music_repeat",
            "Set the active music source's repeat mode: off, track (repeat one), or playlist (repeat the whole context).",
            Schema(
                new() { ["mode"] = Prop("string", "One of: off, track, playlist.") },
                required: ["mode"])),
        NoArgs("get_music_state", "Get the current music playback state: the active source, the available sources, and track/artist, device, volume, progress, shuffle and repeat (isPlaying is false when nothing is playing)."),

        // Sounds (live control + metadata)
        new AiToolDefinition(
            "play_sound",
            "Play one soundboard sound NOW on the server's speakers, overlaying anything already playing (ids from list_sounds). Optional volume 0.0-1.0 overrides the stored level. Errors if the id is unknown or the audio device/file is unavailable.",
            Schema(
                new()
                {
                    ["id"] = Prop("string", "Sound id (from list_sounds)."),
                    ["volume"] = Prop("number", "Optional playback volume 0.0-1.0; omit to use the sound's stored volume."),
                },
                required: ["id"])),
        WithId("stop_sound", "Stop every voice currently playing this sound id (a no-op if it isn't playing)."),
        NoArgs("stop_all_sounds", "Stop all soundboard playback on the server."),
        new AiToolDefinition(
            "update_sound",
            "Update a soundboard sound's editable metadata (name/category/volume/loop). Every field is optional; each omitted field is left unchanged. Does not touch the audio file or tile art. Returns the updated sound.",
            Schema(
                new()
                {
                    ["id"] = Prop("string", "Sound id (from list_sounds)."),
                    ["name"] = Prop("string", "New display name; omit to leave unchanged."),
                    ["category"] = Prop("string", "New category; omit to leave unchanged."),
                    ["volume"] = Prop("number", "New default volume 0.0-1.0; omit to leave unchanged."),
                    ["loop"] = Prop("boolean", "Whether the sound loops; omit to leave unchanged."),
                },
                required: ["id"])),
        NoArgs("get_sounds_state", "Get the ids of the sounds currently playing on the server's soundboard."),

        // Context / control
        NoArgs("list_lights", "List the registered lights (key, name, provider). Use each light's key as a scene light's lightKey or as test_light_fx's lightKey."),
        NoArgs("list_sounds", "List the soundboard sounds (id, name, category, volume, loop, duration). Use each sound's id in a scene's or event's soundEffects."),
        NoArgs("list_spotify_playlists", "List the connected Spotify account's playlists. Each has a spotify: URI usable as a scene's music.playId. Requires Spotify to be connected."),
        new AiToolDefinition(
            "search_spotify_tracks",
            "Search Spotify for tracks matching a query. Each result has a spotify: URI usable as a scene's music.playId. Requires Spotify to be connected.",
            Schema(
                new() { ["query"] = Prop("string", "Search terms, e.g. a song title and/or artist.") },
                required: ["query"])),
        NoArgs("reset_lights", "Reset all lights to the configured default lighting state (the panel's reset-lights button). Errors if no default lighting has been set on the Settings page."),
        NoArgs("get_lights_status", "Get the live bulb state: normalized on/off, mode (colour/white), brightness, colour (RRGGBB hex) and white temperature, plus the raw Tuya/Hue payload for diagnostics. Errors if the light is unreachable."),

        // Party (players + table-level counters)
        NoArgs("list_party", "List the whole live table: players (name, optional portrait, generic counters), the table-level counters (system-wide stats like Fear), the bestiary enemies (reusable statblocks), and system — the active game system's id (e.g. \"daggerheart\", null when none is chosen; it drives the panel's presets, set in Settings, not via tools). Counters are generic {label, value, max, style, key} — the Daggerheart loadout is just a preset."),
        Tool2("upsert_player",
            "Create or replace the party player at the given id (the id arg wins). Returns the stored player.",
            "id", "Player id to save under: a lowercase slug [a-z0-9-_].",
            "member", PlayerShape),
        WithId("delete_player", "Delete the party player with this id (and its portrait). Returns true if it existed, false otherwise."),
        new AiToolDefinition(
            "save_table_counters",
            "Replace the whole set of table-level counters (system-wide stats like Fear that belong to no single player). Returns the stored list.",
            Schema(
                new() { ["counters"] = PropArray(CountersShape) },
                required: ["counters"])),
        new AiToolDefinition(
            "adjust_player_counter",
            "Adjust ONE of a player's counters live (e.g. mark 2 damage), clamped into [0, max]. Pass EXACTLY ONE of delta (bump, may be negative) or value (set absolutely). counter is the counter's key or label (case-insensitive). Returns the updated player; errors if the player or counter is unknown.",
            Schema(
                new()
                {
                    ["id"] = Prop("string", "Player id: a lowercase slug [a-z0-9-_]."),
                    ["counter"] = Prop("string", "The counter's semantic key (preferred, e.g. \"hp\") or its label — both case-insensitive."),
                    ["delta"] = Prop("integer", "Bump the current value by this (may be negative). Give delta OR value, not both."),
                    ["value"] = Prop("integer", "Set the value absolutely. Give value OR delta, not both."),
                },
                required: ["id", "counter"])),
        new AiToolDefinition(
            "adjust_table_counter",
            "Adjust ONE table-level counter live (e.g. +1 Fear), clamped into [0, max]. Pass EXACTLY ONE of delta or value; counter is the counter's key or label (case-insensitive). Returns the updated table-counter list; errors if no counter matches the label.",
            Schema(
                new()
                {
                    ["counter"] = Prop("string", "The counter's semantic key (preferred, e.g. \"fear\") or its label — both case-insensitive."),
                    ["delta"] = Prop("integer", "Bump the current value by this (may be negative). Give delta OR value, not both."),
                    ["value"] = Prop("integer", "Set the value absolutely. Give value OR delta, not both."),
                },
                required: ["counter"])),

        // Bestiary enemies (reusable statblocks)
        Tool2("upsert_enemy",
            "Create or replace a bestiary enemy statblock at the given id (the id arg wins). A statblock is base stats only — no live tracking. Returns the stored statblock.",
            "id", "Enemy id to save under: a lowercase slug [a-z0-9-_].",
            "enemy", EnemyShape),
        WithId("delete_enemy", "Delete the bestiary enemy statblock with this id (and its portrait). Returns true if it existed, false otherwise. Encounter instances already made from it are unaffected (they are snapshots)."),
        new AiToolDefinition(
            "adjust_enemy_counter",
            "Adjust ONE of a bestiary enemy statblock's BASE counters, clamped into [0, max]. This edits the template's starting values, NOT a live fight (use adjust_encounter_enemy for that). Pass EXACTLY ONE of delta or value; counter is the key or label (case-insensitive). Returns the updated statblock; errors if the enemy or counter is unknown.",
            Schema(
                new()
                {
                    ["id"] = Prop("string", "Enemy id: a lowercase slug [a-z0-9-_]."),
                    ["counter"] = Prop("string", "The counter's semantic key (preferred, e.g. \"hp\") or its label — both case-insensitive."),
                    ["delta"] = Prop("integer", "Bump the current value by this (may be negative). Give delta OR value, not both."),
                    ["value"] = Prop("integer", "Set the value absolutely. Give value OR delta, not both."),
                },
                required: ["id", "counter"])),

        // Encounters (prepped fights)
        NoArgs("list_encounters", "List every saved encounter (full entities). An encounter is a prepped fight: heroIds (party player ids; empty = all players), enemy instances (each a live copy of a bestiary statblock with its own counters + per-instance spotlight/hidden flags), an optional background image, and optional scene/event ids activated when it runs."),
        WithId("get_encounter", "Get one encounter by id, or null if none has that id."),
        Tool2("upsert_encounter",
            "Create or replace the encounter at the given id (the id arg wins). Returns the stored encounter.",
            "id", "Encounter id to save under: a lowercase slug [a-z0-9-_].",
            "encounter", EncounterShape),
        WithId("delete_encounter", "Delete the encounter with this id (and its owned background image). Clears the /tv display if this encounter was showing. Returns true if it existed, false otherwise."),
        WithId("run_encounter", "Run the encounter NOW: activate its configured scene and event best-effort (each reports ok/skipped/notFound/partial/error but never blocks) and push its heroes-left / enemies-right view to the /tv display. Returns { rev, encounter, scene, event }. Errors if the encounter id is unknown."),
        new AiToolDefinition(
            "adjust_encounter_enemy",
            "Adjust ONE counter of ONE enemy instance in a prepped/running fight (e.g. mark 3 damage on the boss), clamped into [0, max]. Pass EXACTLY ONE of delta or value; counter is the counter's key or label (case-insensitive). Returns the updated encounter; errors if the encounter, instance or counter is unknown.",
            Schema(
                new()
                {
                    ["id"] = Prop("string", "Encounter id: a lowercase slug [a-z0-9-_]."),
                    ["instanceId"] = Prop("string", "The enemy instance's instanceId within that encounter."),
                    ["counter"] = Prop("string", "The counter's semantic key (preferred, e.g. \"hp\") or its label — both case-insensitive."),
                    ["delta"] = Prop("integer", "Bump the current value by this (may be negative). Give delta OR value, not both."),
                    ["value"] = Prop("integer", "Set the value absolutely. Give value OR delta, not both."),
                },
                required: ["id", "instanceId", "counter"])),
        WithId("reset_encounter", "Reset every enemy instance's counters to its statblock's starting values (before re-running the same fight). Returns the updated encounter. Errors if the encounter id is unknown."),

        // Boards (composable player-facing TV layouts)
        NoArgs("list_boards", "List every saved board (full entities). A board is a 16:9 player-facing TV layout: a background colour or image plus positioned elements (image/text or a live party/enemies roster), all in percent-of-stage coordinates."),
        WithId("get_board", "Get one board by id, or null if none has that id."),
        Tool2("upsert_board",
            "Create or replace the board at the given id (the id arg wins). Returns the stored board.",
            "id", "Board id to save under: a lowercase slug [a-z0-9-_].",
            "board", BoardShape),
        WithId("delete_board", "Delete the board with this id (and its owned images). Clears the /tv display if this board was showing. Returns true if it existed, false otherwise."),

        // TV (the player-facing display)
        new AiToolDefinition(
            "show_on_tv",
            "Push content to the player-facing /tv display. Pass EXACTLY ONE of image (a stored image name — a handout/map), board (a board id from list_boards), or encounter (an encounter id from list_encounters — shows its heroes-left / enemies-right view). Optional label overrides the caption. Returns the new display rev. Errors if none / more than one target is given, or the target doesn't exist.",
            Schema(
                new()
                {
                    ["image"] = Prop("string", "A stored image file name to show; omit unless showing an image."),
                    ["board"] = Prop("string", "A board id to show; omit unless showing a board."),
                    ["encounter"] = Prop("string", "An encounter id to show; omit unless showing an encounter."),
                    ["label"] = Prop("string", "Optional caption; defaults to the board's/encounter's name."),
                },
                required: [])),
        NoArgs("clear_tv", "Clear the player-facing /tv display (nothing shown). Returns the new display rev."),
        NoArgs("get_tv_state", "Get what the /tv display is currently showing: the live rev plus the pushed content's kind (image|board|encounter), its ref (image name or entity id) and label — all null when the display is cleared."),
    ];

    // ---- Dispatch ----

    /// <summary>
    /// Run one tool call. Returns the JSON result (serialized with the wire options) plus an is-error flag —
    /// validation/integration failures come back as (message, true) so the model can correct itself.
    /// </summary>
    public async Task<(string ResultJson, bool IsError)> ExecuteAsync(
        string name, IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        try
        {
            object? result = name switch
            {
                "list_scenes" => await tools.ListScenesAsync(),
                "get_scene" => await tools.GetSceneAsync(Id(input)),
                "upsert_scene" => await tools.UpsertSceneAsync(Entity<Scene>(input, "scene"), Id(input)),
                "delete_scene" => await tools.DeleteSceneAsync(Id(input)),
                "activate_scene" => await tools.ActivateSceneAsync(Id(input)),
                "get_active_scene" => tools.GetActiveScene(),

                "list_events" => await tools.ListEventsAsync(),
                "get_event" => await tools.GetEventAsync(Id(input)),
                "upsert_event" => await tools.UpsertEventAsync(Entity<GameEvent>(input, "evt"), Id(input)),
                "delete_event" => await tools.DeleteEventAsync(Id(input)),
                "trigger_event" => await tools.TriggerEventAsync(Id(input)),
                "stop_event" => tools.StopEvent(),
                "get_event_state" => tools.GetEventState(),

                "list_screens" => await tools.ListScreensAsync(),
                "get_screen" => await tools.GetScreenAsync(Id(input)),
                "upsert_screen" => await tools.UpsertScreenAsync(Entity<Screen>(input, "screen"), Id(input)),
                "delete_screen" => await tools.DeleteScreenAsync(Id(input)),

                "list_light_fx" => await tools.ListLightFxAsync(),
                "get_light_fx" => await tools.GetLightFxAsync(Id(input)),
                "upsert_light_fx" => await tools.UpsertLightFxAsync(Entity<LightFx>(input, "fx"), Id(input)),
                "delete_light_fx" => await tools.DeleteLightFxAsync(Id(input)),
                "test_light_fx" => await tools.TestLightFxAsync(Id(input), OptStr(input, "lightKey"), OptInt(input, "seconds") ?? 10),
                "stop_light_fx_test" => tools.StopLightFxTest(),

                "play_music" => await tools.PlayMusicAsync(Str(input, "uri")),
                "pause_music" => await tools.PauseMusicAsync(),
                "resume_music" => await tools.ResumeMusicAsync(),
                "next_track" => await tools.NextTrackAsync(),
                "previous_track" => await tools.PreviousTrackAsync(),
                "set_music_volume" => await tools.SetMusicVolumeAsync(Dbl(input, "value")),
                "set_music_shuffle" => await tools.SetMusicShuffleAsync(Bool(input, "enabled")),
                "set_music_repeat" => await tools.SetMusicRepeatAsync(Str(input, "mode")),
                "get_music_state" => await tools.GetMusicStateAsync(),

                "play_sound" => await tools.PlaySoundAsync(Id(input), OptDbl(input, "volume")),
                "stop_sound" => tools.StopSound(Id(input)),
                "stop_all_sounds" => tools.StopAllSounds(),
                "update_sound" => await tools.UpdateSoundAsync(
                    Id(input), OptStr(input, "name"), OptStr(input, "category"), OptDbl(input, "volume"), OptBool(input, "loop")),
                "get_sounds_state" => tools.GetSoundsState(),

                "list_lights" => tools.ListLights(),
                "list_sounds" => await tools.ListSoundsAsync(),
                "list_spotify_playlists" => await tools.ListSpotifyPlaylistsAsync(),
                "search_spotify_tracks" => await tools.SearchSpotifyTracksAsync(Str(input, "query")),
                "reset_lights" => await ResetAsync(),
                "get_lights_status" => await tools.GetLightsStatusAsync(),

                "list_party" => await tools.ListPartyAsync(),
                "upsert_player" => await tools.UpsertPlayerAsync(Entity<PartyMember>(input, "member"), Id(input)),
                "delete_player" => await tools.DeletePlayerAsync(Id(input)),
                "save_table_counters" => await tools.SaveTableCountersAsync(Entity<List<PartyCounter>>(input, "counters")),
                "adjust_player_counter" => await tools.AdjustPlayerCounterAsync(
                    Id(input), Str(input, "counter"), OptInt(input, "delta"), OptInt(input, "value")),
                "adjust_table_counter" => await tools.AdjustTableCounterAsync(
                    Str(input, "counter"), OptInt(input, "delta"), OptInt(input, "value")),

                "upsert_enemy" => await tools.UpsertEnemyAsync(Entity<Enemy>(input, "enemy"), Id(input)),
                "delete_enemy" => await tools.DeleteEnemyAsync(Id(input)),
                "adjust_enemy_counter" => await tools.AdjustEnemyCounterAsync(
                    Id(input), Str(input, "counter"), OptInt(input, "delta"), OptInt(input, "value")),

                "list_encounters" => await tools.ListEncountersAsync(),
                "get_encounter" => await tools.GetEncounterAsync(Id(input)),
                "upsert_encounter" => await tools.UpsertEncounterAsync(Entity<Encounter>(input, "encounter"), Id(input)),
                "delete_encounter" => await tools.DeleteEncounterAsync(Id(input)),
                "run_encounter" => await tools.RunEncounterAsync(Id(input)),
                "adjust_encounter_enemy" => await tools.AdjustEncounterEnemyAsync(
                    Id(input), Str(input, "instanceId"), Str(input, "counter"), OptInt(input, "delta"), OptInt(input, "value")),
                "reset_encounter" => await tools.ResetEncounterAsync(Id(input)),

                "list_boards" => await tools.ListBoardsAsync(),
                "get_board" => await tools.GetBoardAsync(Id(input)),
                "upsert_board" => await tools.UpsertBoardAsync(Entity<Board>(input, "board"), Id(input)),
                "delete_board" => await tools.DeleteBoardAsync(Id(input)),

                "show_on_tv" => await tools.ShowOnTvAsync(
                    OptStr(input, "image"), OptStr(input, "board"), OptStr(input, "encounter"), OptStr(input, "label")),
                "clear_tv" => tools.ClearTv(),
                "get_tv_state" => tools.GetTvState(),

                _ => throw new ArgumentException($"Unknown tool '{name}'."),
            };
            return (JsonSerializer.Serialize(result, AiJson.Options), false);
        }
        // The same failure modes the HTTP endpoints raise: bad input/validation (ValidationException is an
        // ArgumentException), unknown entity/counter (NotFoundException — the party/encounter adjust ops surface
        // it), not-configured (NotConfiguredException is an InvalidOperationException), provider errors,
        // unreachable hardware/Spotify. Surface the message to the model as an error tool-result so it can
        // retry/correct instead of the run crashing.
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotFoundException
            or SpotifyException or HueException or SoundboardException
            or SocketException or IOException or TimeoutException
            or HttpRequestException or TaskCanceledException && ct.IsCancellationRequested == false)
        {
            return (ex.Message, true);
        }
    }

    private async Task<object> ResetAsync()
    {
        await tools.ResetLightsAsync();
        return new { ok = true };
    }

    // ---- Argument helpers ----

    private static string Id(IReadOnlyDictionary<string, JsonElement> input) => Str(input, "id");

    private static string Str(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } s
            ? s
            : throw new ArgumentException($"The '{key}' argument is required.");

    private static string? OptStr(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int? OptInt(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;

    private static double Dbl(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : throw new ArgumentException($"The '{key}' argument (a number) is required.");

    private static double? OptDbl(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;

    private static bool Bool(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : throw new ArgumentException($"The '{key}' argument (true or false) is required.");

    private static bool? OptBool(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : null;

    private static T Entity<T>(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new ArgumentException($"The '{key}' argument (the entity JSON) is required.");
        return el.Deserialize<T>(AiJson.Options)
               ?? throw new ArgumentException($"The '{key}' argument could not be read as an entity.");
    }

    // ---- Schema builders ----

    private static AiToolDefinition NoArgs(string name, string description) =>
        new(name, description, Schema(new(), required: []));

    private static AiToolDefinition WithId(string name, string description) =>
        new(name, description, Schema(
            new() { ["id"] = Prop("string", "A lowercase slug [a-z0-9-_].") },
            required: ["id"]));

    private static AiToolDefinition Tool2(string name, string description, string aName, string aDesc, string bName, string bDesc) =>
        new(name, description, Schema(
            new()
            {
                [aName] = Prop("string", aDesc),
                [bName] = Prop("object", bDesc),
            },
            required: [aName, bName]));

    private static JsonElement Schema(Dictionary<string, JsonElement> properties, IReadOnlyList<string> required) =>
        // A JSON-Schema object every provider adapter understands: { type, properties, required }.
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties,
            required,
        });

    private static JsonElement Prop(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });

    // An array-typed argument. Includes a minimal object `items` schema — Gemini/OpenAI reject a bare
    // `type: array` with no items (the shape the model actually sends lives in the description, like the
    // object-typed entity args above).
    private static JsonElement PropArray(string description) =>
        JsonSerializer.SerializeToElement(new { type = "array", description, items = new { type = "object" } });
}
