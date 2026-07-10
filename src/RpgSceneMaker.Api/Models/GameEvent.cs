namespace RpgSceneMaker.Api.Models;

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
