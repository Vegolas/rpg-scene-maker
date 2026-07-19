namespace AmbientDirector.Api.Models;

// Wire contract: the API serializes these event types straight to /events (there is no Contracts/ DTO for an
// event; Contracts/EventContracts.cs only holds the state/stop shapes). The panel mirrors this exact shape by
// hand in AmbientDirector.Ui/Contracts/EventContracts.cs (EventDto + flash/after/timeline clips) — keep in sync.

/// <summary>
/// A one-shot triggered effect: a brief light flash and/or one or more sound effects (e.g. thunder =
/// a white flash + a thunderclap). Unlike a scene it isn't a persistent state — it fires <em>over</em>
/// the current scene and, for the flash, returns to it. Named <c>GameEvent</c> to avoid clashing with
/// the <c>event</c> keyword / <c>EventHandler</c>.
/// </summary>
public class GameEvent
{
    /// <summary>Slug id (matched case-insensitively, like scenes/sounds) used in <c>/events/{id}/trigger</c> URLs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Optional brief light flash. Null = the event doesn't touch the lights.</summary>
    public EventFlash? Flash { get; set; }

    /// <summary>Ids of soundboard <see cref="Sound"/>s fired when this event is triggered. Unlike a scene,
    /// these overlap current playback (a clap over the music) instead of replacing it.</summary>
    public List<string> SoundEffects { get; set; } = [];

    /// <summary>Stored file name of an optional full-art tile background (uploaded via <c>/images</c>), or null.</summary>
    public string? Image { get; set; }

    /// <summary>Optional advanced timeline: sound and light clips placed at millisecond offsets, played in
    /// the background when the event is triggered. Null on legacy events (Flash + <see cref="SoundEffects"/>).</summary>
    public EventTimeline? Timeline { get; set; }

    /// <summary>What the lighting does when the event finishes. Null (and <see cref="EventAfter.Mode"/>
    /// "previous") means the historical behavior: restore whatever was showing before — the live scene, else
    /// the configured default light. Can instead fully activate another scene or apply the default light.</summary>
    public EventAfter? After { get; set; }
}

/// <summary>What an event does to the lighting once it finishes. <see cref="Mode"/> is a small string
/// (like the scene light-clip modes) so it serializes identically over the wire and in the JSON column:
/// "previous" (restore the prior lighting — the live scene, else the default light — the default), "scene"
/// (fully activate <see cref="SceneId"/>, lights + music, like tapping it) or "default" (apply the
/// configured default light regardless of the live scene).</summary>
public class EventAfter
{
    public string Mode { get; set; } = "previous";

    /// <summary>Scene id to activate when <see cref="Mode"/> is "scene"; ignored otherwise.</summary>
    public string? SceneId { get; set; }
}

/// <summary>A background timeline of sound and light clips triggered together, each placed at an offset.</summary>
public class EventTimeline
{
    public List<TimelineSoundClip> Sounds { get; set; } = [];
    public List<TimelineLightClip> Lights { get; set; } = [];
}

/// <summary>A sound clip on the timeline: play a soundboard <see cref="Sound"/> at an offset.</summary>
public class TimelineSoundClip
{
    /// <summary>Id of the soundboard <see cref="Sound"/> to play.</summary>
    public string SoundId { get; set; } = "";

    /// <summary>Offset from the timeline start, in milliseconds.</summary>
    public int StartMs { get; set; }

    /// <summary>How long to play before stopping the clip, in milliseconds. Null = play to the file's natural end.</summary>
    public int? DurationMs { get; set; }

    /// <summary>Playback volume 0.0 - 1.0. Null = use the sound's own stored volume.</summary>
    public double? Volume { get; set; }
}

/// <summary>A light clip on the timeline: hold a static state (or run an effect) for a window at an offset.</summary>
public class TimelineLightClip
{
    /// <summary>Registry key of the light. Null or empty = "all lights" via the configured provider group (like the legacy scene <c>Light</c> block).</summary>
    public string? LightKey { get; set; }

    /// <summary>Offset from the timeline start, in milliseconds.</summary>
    public int StartMs { get; set; }

    /// <summary>How long the clip holds before the light is left / restored, in milliseconds.</summary>
    public int DurationMs { get; set; }

    public bool? Power { get; set; }

    /// <summary>Hex color like "#FF8C2A". When set, the light switches to colour mode.</summary>
    public string? Color { get; set; }

    /// <summary>0-100.</summary>
    public int? Brightness { get; set; }

    /// <summary>White color temperature, 0 (warm) - 100 (cold). Used when no Color is set.</summary>
    public int? Temperature { get; set; }

    /// <summary>When set, the light is animated for the clip's window instead of held at a static state.</summary>
    public LightEffect? Effect { get; set; }
}

/// <summary>A brief light flash: jump the lights to a colour/brightness, hold, then restore the prior lighting.</summary>
public class EventFlash
{
    /// <summary>Hex colour like "#FFFFFF" the lights flash to.</summary>
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>0-100.</summary>
    public int Brightness { get; set; } = 100;

    /// <summary>How long the flash is held before the prior lighting is restored, in milliseconds.</summary>
    public int DurationMs { get; set; } = 200;
}
