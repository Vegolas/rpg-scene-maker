namespace RpgSceneMaker.Api.Models;

// Wire contract: the API serializes LightFx straight to /lightfx (there is no Contracts/ DTO). The panel
// mirrors this exact shape by hand in RpgSceneMaker.Ui/Contracts/LightFxContracts.cs (LightFxDto) — keep in sync.

/// <summary>
/// A named, reusable "Light FX" in the library: a hand-authored keyframe sequence (identical in shape to a
/// "custom" <see cref="LightEffect"/>) that scene lights and event-timeline light clips reference by id
/// (<see cref="LightEffect.FxId"/>, type "fx") instead of embedding their own keyframes. Resolved to a
/// concrete "custom" effect at apply time; deleting an FX detaches every reference into an embedded copy.
/// </summary>
public class LightFx
{
    /// <summary>Slug id (matched case-insensitively, like scenes/sounds) used in <c>/lightfx/{id}/…</c> URLs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The keyframe sequence, same semantics as a "custom" <see cref="LightEffect.Keyframes"/>.</summary>
    public List<LightKeyframe> Keyframes { get; set; } = [];

    /// <summary>When true the keyframe cycle repeats forever; when false it plays once and holds the last keyframe.</summary>
    public bool Loop { get; set; }

    /// <summary>Total cycle length in ms. Required when <see cref="Loop"/> is true (must be ≥ last keyframe
    /// <see cref="LightKeyframe.AtMs"/> + 100). Null when not looping.</summary>
    public int? CycleMs { get; set; }
}
