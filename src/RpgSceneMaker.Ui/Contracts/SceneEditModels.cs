namespace RpgSceneMaker.Ui.Contracts;

// Mutable form model for the scene editor — inputs bind straight to these; converts to the wire DTO on save.
public class SceneEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public LightEdit? Light { get; set; }
    // Per-light entries; skip rows convert to null and are dropped in ToDto().
    public List<SceneLightEdit> Lights { get; set; } = [];
    public MusicEdit? Music { get; set; }
    public List<string> SoundEffects { get; set; } = [];

    public SceneDto ToDto() => new(Id, Name,
        Light is null ? null : new LightDto(Light.Power, Light.Color, Light.Brightness, Light.Temperature),
        Lights.Select(l => l.ToDto()).OfType<SceneLightDto>().ToList(),
        Music is null ? null : new MusicDto(Music.PlayId, Music.Volume, Music.Pause),
        SoundEffects);
}

public class LightEdit
{
    public bool? Power { get; set; }
    public string? Color { get; set; }
    public int? Brightness { get; set; }
    public int? Temperature { get; set; }
}

// Mutable form model for one per-light row in the scene editor.
public class SceneLightEdit
{
    public string LightKey { get; set; } = "";
    // "skip" | "color" | "white" | "off"
    public string Mode { get; set; } = "skip";
    public string Color { get; set; } = "#ff8c2a";
    public int Brightness { get; set; } = 80;
    public int Temperature { get; set; } = 40;
    public EffectEdit Effect { get; set; } = new();

    // Skip rows return null so they are omitted from the wire scene. Effects only ride on color/white.
    public SceneLightDto? ToDto() => Mode switch
    {
        "color" => new SceneLightDto(LightKey, true, Brightness, Color, null, Effect.ToDto()),
        "white" => new SceneLightDto(LightKey, true, Brightness, null, Temperature, Effect.ToDto()),
        "off" => new SceneLightDto(LightKey, false, null, null, null, null),
        _ => null,
    };
}

// Mutable form model for a per-light effect.
public class EffectEdit
{
    // "none" | "flicker" | "glow" | "storm" | "drift"
    public string Type { get; set; } = "none";
    public int Speed { get; set; } = 5;
    public int Intensity { get; set; } = 5;
    public List<string> Colors { get; set; } = [];

    public EffectDto? ToDto() =>
        Type == "none" ? null : new EffectDto(Type, Speed, Intensity, [.. Colors]);
}

public class MusicEdit
{
    public string? PlayId { get; set; }
    public double? Volume { get; set; }
    public bool Pause { get; set; }
}
