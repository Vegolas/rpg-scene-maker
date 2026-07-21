namespace AmbientDirector.Api.Services.Systems;

/// <summary>
/// A deliberately minimal Dungeons &amp; Dragons 5e sample (issue #129; docs/GAME-SYSTEMS.md § "The D&amp;D 5e
/// sample"). It is <b>documentation, not a full 5e implementation</b>: the copy-me template for a contributor
/// adding a system (see the "Adding a new system" quick guide). Its emptiness is load-bearing — by omitting
/// <see cref="Quickbar"/> and <see cref="SpotlightLabel"/> (both default interface members) and shipping no
/// table counters, it proves every contract feature beyond the four required members is optional and that the
/// party/bestiary/TV layer is not Daggerheart-shaped.
/// </summary>
public sealed class Dnd5eSystem : IGameSystem
{
    public string Id => "dnd5e";

    public string NameKey => "system.dnd5e.name";

    // Numbers, not pips: a 5e character's hit points and armor class are tracked as plain values, so these use
    // the "number" style (no max — the renderer shows a bare number). HP reuses the shared party.preset.hp key.
    public IReadOnlyList<CounterPreset> MemberCounters { get; } =
    [
        new("hp", "party.preset.hp", Style: "number"),
        new("ac", "party.preset.ac", Style: "number"),
    ];

    public IReadOnlyList<CounterPreset> EnemyCounters { get; } =
    [
        new("hp", "party.preset.hp", Style: "number"),
    ];

    // No table-level stats (nothing like Daggerheart's Fear), an empty quickbar and no spotlight label — the
    // latter two inherited from IGameSystem's defaults, which is the whole point of this sample.
    public IReadOnlyList<CounterPreset> TableCounters { get; } = [];
}
