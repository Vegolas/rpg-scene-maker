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
    public string? Image { get; set; }

    public SceneDto ToDto() => new(Id, Name,
        Light is null ? null : new LightDto(Light.Power, Light.Color, Light.Brightness, Light.Temperature),
        Lights.Select(l => l.ToDto()).OfType<SceneLightDto>().ToList(),
        Music is null ? null : new MusicDto(Music.PlayId, Music.Volume, Music.Pause),
        SoundEffects,
        Image);
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
    // "none" | "flicker" | "glow" | "storm" | "drift" | "custom"
    public string Type { get; set; } = "none";
    public int Speed { get; set; } = 5;
    public int Intensity { get; set; } = 5;
    public List<string> Colors { get; set; } = [];

    // "custom" only: hand-authored keyframe sequence + looping.
    public List<KeyframeEdit> Keyframes { get; set; } = [];
    public bool Loop { get; set; }
    public int? CycleMs { get; set; }

    public EffectDto? ToDto() => Type switch
    {
        "none" => null,
        "custom" => new EffectDto("custom", Speed, Intensity, [.. Colors],
            Keyframes.Select(k => k.ToDto()).ToList(), Loop, Loop ? CycleMs : null),
        _ => new EffectDto(Type, Speed, Intensity, [.. Colors]),
    };

    // Deep copy (used by the scene editor's "copy to all lights").
    public EffectEdit Clone() => new()
    {
        Type = Type,
        Speed = Speed,
        Intensity = Intensity,
        Colors = [.. Colors],
        Keyframes = Keyframes.Select(k => k.Clone()).ToList(),
        Loop = Loop,
        CycleMs = CycleMs,
    };

    // Build an editable effect from a wire DTO (null → "none"). Shared by the scene editor and the timeline
    // clip inspector so keyframe/loop fields can never be dropped by only one of the two mappings.
    public static EffectEdit FromDto(EffectDto? fx) => fx is null
        ? new EffectEdit()
        : new EffectEdit
        {
            Type = fx.Type,
            Speed = fx.Speed,
            Intensity = fx.Intensity,
            Colors = [.. fx.Colors ?? []],
            Keyframes = (fx.Keyframes ?? []).Select(KeyframeEdit.FromDto).ToList(),
            Loop = fx.Loop,
            CycleMs = fx.CycleMs,
        };
}

// Mutable form model for one keyframe of a "custom" effect. Mode derives the wire power/color/temperature,
// mirroring the scene/timeline light-clip modes.
public class KeyframeEdit
{
    // Transient per-session id so the keyframe editor can address a row stably; not persisted.
    public string Uid { get; } = Guid.NewGuid().ToString("N");
    public int AtMs { get; set; }
    // "color" | "white" | "off"
    public string Mode { get; set; } = "color";
    public string Color { get; set; } = "#ffffff";
    public int Brightness { get; set; } = 100;
    public int Temperature { get; set; } = 40;
    // Fade duration in ms (Hue only; Tuya ignores). Null = provider default. New keyframes default to
    // instant — snappy sequences (strobes) are the primary use; "Default" (~400 ms) is a pick away.
    public int? TransitionMs { get; set; } = 0;

    public KeyframeDto ToDto() => Mode switch
    {
        "white" => new KeyframeDto(AtMs, true, null, Brightness, Temperature, TransitionMs),
        "off" => new KeyframeDto(AtMs, false, null, null, null, TransitionMs),
        _ => new KeyframeDto(AtMs, true, Color, Brightness, null, TransitionMs),
    };

    public KeyframeEdit Clone() => new()
    {
        AtMs = AtMs,
        Mode = Mode,
        Color = Color,
        Brightness = Brightness,
        Temperature = Temperature,
        TransitionMs = TransitionMs,
    };

    public static KeyframeEdit FromDto(KeyframeDto d)
    {
        var edit = new KeyframeEdit
        {
            AtMs = d.AtMs,
            Brightness = d.Brightness ?? 100,
            Temperature = d.Temperature ?? 40,
            TransitionMs = d.TransitionMs,
        };
        if (d.Color is { } color) { edit.Mode = "color"; edit.Color = color; }
        else if (d.Power == false) { edit.Mode = "off"; }
        else { edit.Mode = "white"; }
        return edit;
    }
}

public class MusicEdit
{
    public string? PlayId { get; set; }
    public double? Volume { get; set; }
    public bool Pause { get; set; }
}
