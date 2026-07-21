namespace AmbientDirector.Api.Services.Systems;

/// <summary>
/// Daggerheart — the system this app grew up with. The presets below are the exact values the panel's old
/// hardcoded UI presets used (PartyEditor / PartyEnemyEditor / Encounters "Add Fear" + create-enemy seed) and
/// the glyph/colour pairs match today's <c>BoardCanvas</c>/<c>_party.scss</c> rendering — the reference table
/// in <c>docs/GAME-SYSTEMS.md</c> is the source of truth; a Daggerheart table must render identically across
/// the refactor phases.
/// </summary>
public sealed class DaggerheartSystem : IGameSystem
{
    public string Id => "daggerheart";

    public string NameKey => "system.daggerheart.name";

    public IReadOnlyList<CounterPreset> MemberCounters { get; } =
    [
        new("hp", "party.preset.hp", Value: 0, Max: 6, Style: "pips", Glyph: GameSystemGlyphs.HeartBroken, Color: "#ff4d5e"),
        new("stress", "party.preset.stress", Value: 0, Max: 6, Style: "pips", Glyph: GameSystemGlyphs.HeartDark),
        new("armor", "party.preset.armor", Value: 0, Max: 3, Style: "pips", Glyph: GameSystemGlyphs.ShieldBroken, Color: "#cdd4e0"),
        new("hope", "party.preset.hope", Value: 2, Max: 6, Style: "pips", Glyph: GameSystemGlyphs.Diamond, Color: "#eef2f8"),
    ];

    public IReadOnlyList<CounterPreset> EnemyCounters { get; } =
    [
        new("hp", "party.preset.hp", Value: 0, Max: 6, Style: "pips", Glyph: GameSystemGlyphs.HeartBroken, Color: "#ff4d5e"),
        new("stress", "party.preset.stress", Value: 0, Max: 3, Style: "pips", Glyph: GameSystemGlyphs.HeartDark),
    ];

    public IReadOnlyList<CounterPreset> TableCounters { get; } =
    [
        // Fear renders as a plain (red) dot pip — deliberately no glyph, matching the pre-refactor TV.
        new("fear", "party.preset.fear", Value: 0, Max: 12, Style: "pips", Color: "#ff4d2e"),
    ];

    public IReadOnlyList<string> Quickbar { get; } = ["fear"];

    public string? SpotlightLabel => "SPOTLIGHT";
}
