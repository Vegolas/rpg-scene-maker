using System.Net.Sockets;
using System.Text.Json;
using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Services.Ai.Providers;

namespace RpgSceneMaker.Api.Services.Ai;

/// <summary>
/// The in-panel assistant's tool surface: the same 23 façade operations the MCP server exposes, but as
/// provider-neutral <see cref="AiToolDefinition"/>s plus an executor. <see cref="Definitions"/> is the
/// static schema list sent to the model on every turn (each provider adapter maps it to its own SDK's tool
/// type); <see cref="ExecuteAsync"/> dispatches a tool call back onto <see cref="AiToolService"/>.
/// Integration/validation failures are caught here and returned as error tool-results (IsError=true) so the
/// model can read the message and self-correct instead of the run crashing. Tool names, param names and
/// entity JSON shapes match the MCP tools byte-for-byte (camelCase via <see cref="AiJson"/>), so both
/// surfaces behave identically.
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

    /// <summary>The 23 tool definitions, matching the MCP tool names and the façade operations 1:1.</summary>
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

                "list_light_fx" => await tools.ListLightFxAsync(),
                "get_light_fx" => await tools.GetLightFxAsync(Id(input)),
                "upsert_light_fx" => await tools.UpsertLightFxAsync(Entity<LightFx>(input, "fx"), Id(input)),
                "delete_light_fx" => await tools.DeleteLightFxAsync(Id(input)),
                "test_light_fx" => await tools.TestLightFxAsync(Id(input), OptStr(input, "lightKey"), OptInt(input, "seconds") ?? 10),
                "stop_light_fx_test" => tools.StopLightFxTest(),

                "list_lights" => tools.ListLights(),
                "list_sounds" => await tools.ListSoundsAsync(),
                "list_spotify_playlists" => await tools.ListSpotifyPlaylistsAsync(),
                "search_spotify_tracks" => await tools.SearchSpotifyTracksAsync(Str(input, "query")),
                "reset_lights" => await ResetAsync(),

                _ => throw new ArgumentException($"Unknown tool '{name}'."),
            };
            return (JsonSerializer.Serialize(result, AiJson.Options), false);
        }
        // The same failure modes the HTTP endpoints raise: bad input/validation, provider errors, unreachable
        // hardware/Spotify. Surface the message to the model as an error tool-result so it can retry/correct.
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
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
}
