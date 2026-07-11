namespace RpgSceneMaker.Ui.Contracts;

// Mirrors the API's LightFx shape (contracts are duplicated per project by design — keep in sync by hand).
// A named, reusable keyframe sequence (identical to a "custom" effect) referenced by scene lights and event
// timeline light clips via EffectDto.FxId (type "fx").
public record LightFxDto(string Id, string Name, List<KeyframeDto>? Keyframes, bool Loop, int? CycleMs);

// Mutable form model for the Effects editor. The keyframe sequence rides on an EffectEdit (Type "custom") so
// the shared KeyframeEditor component can bind straight to it.
public class LightFxEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public EffectEdit Fx { get; set; } = new() { Type = "custom" };

    public LightFxDto ToDto() => new(Id, Name,
        Fx.Keyframes.Select(k => k.ToDto()).ToList(), Fx.Loop, Fx.Loop ? Fx.CycleMs : null);

    public static LightFxEdit FromDto(LightFxDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Fx = new EffectEdit
        {
            Type = "custom",
            Keyframes = (d.Keyframes ?? []).Select(KeyframeEdit.FromDto).ToList(),
            Loop = d.Loop,
            CycleMs = d.CycleMs,
        },
    };
}
