namespace RpgSceneMaker.Api.Models;

/// <summary>A named table state: lighting + music + one-shot sound effects.</summary>
public class Scene
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Legacy "all lights" mode, still written by the editor's simple mode. Applied to the configured provider group.</summary>
    public LightSettings? Light { get; set; }

    /// <summary>Per-light control. When non-empty this takes over from <see cref="Light"/>: each entry targets a registered light by key and may run an effect.</summary>
    public List<SceneLight> Lights { get; set; } = [];

    public MusicSettings? Music { get; set; }

    /// <summary>Ids of soundboard <see cref="Sound"/>s fired when this scene is activated. Activating a
    /// scene that has any entries stops current playback first, then plays these with their own volume/loop.</summary>
    public List<string> SoundEffects { get; set; } = [];
}

/// <summary>A scene's settings for one registered light, optionally animated by an effect.</summary>
public class SceneLight
{
    /// <summary>Registry key of the light this entry controls.</summary>
    public string LightKey { get; set; } = "";
    public bool? Power { get; set; }

    /// <summary>Hex color like "#FF8C2A". When set, the light switches to colour mode.</summary>
    public string? Color { get; set; }

    /// <summary>0-100.</summary>
    public int? Brightness { get; set; }

    /// <summary>White color temperature, 0 (warm) - 100 (cold). Used when no Color is set.</summary>
    public int? Temperature { get; set; }

    /// <summary>When set, the light is animated in the background instead of held at a static state.</summary>
    public LightEffect? Effect { get; set; }
}

/// <summary>A background animation applied to a single light.</summary>
public class LightEffect
{
    /// <summary>"flicker" | "glow" | "storm" | "drift".</summary>
    public string Type { get; set; } = "";

    /// <summary>1 (slow) - 10 (fast).</summary>
    public int Speed { get; set; } = 5;

    /// <summary>1 (subtle) - 10 (extreme).</summary>
    public int Intensity { get; set; } = 5;

    /// <summary>Hex colors. Required (≥2) for "drift"; "storm" uses the first as its flash color (else cold white).</summary>
    public List<string> Colors { get; set; } = [];
}

public class LightSettings
{
    public bool? Power { get; set; }

    /// <summary>Hex color like "#FF8C2A". When set, the bulb switches to colour mode.</summary>
    public string? Color { get; set; }

    /// <summary>0-100.</summary>
    public int? Brightness { get; set; }

    /// <summary>White color temperature, 0 (warm) - 100 (cold). When set (and no Color), the bulb switches to white mode.</summary>
    public int? Temperature { get; set; }
}

public class MusicSettings
{
    /// <summary>Spotify track/playlist/album/artist URI or open.spotify.com link. Leave null to keep current music.</summary>
    public string? PlayId { get; set; }

    /// <summary>0.0 - 1.0.</summary>
    public double? Volume { get; set; }

    /// <summary>Pause whatever is playing instead of starting something.</summary>
    public bool Pause { get; set; }
}
