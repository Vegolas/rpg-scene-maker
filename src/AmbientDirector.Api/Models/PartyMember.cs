namespace AmbientDirector.Api.Models;

// Wire contract: the API serializes PartyMember/PartyCounter straight to /party (there is no Contracts/ DTO
// beyond the PartyDto envelope, like Board/Screen). The panel mirrors this exact shape by hand in
// AmbientDirector.Ui/Contracts/PartyContracts.cs — keep the two in sync when a field changes.

/// <summary>
/// One person at the table whose live stats appear on the shared TV (issue #88, Phase 3). Named
/// <c>PartyMember</c>, not <c>Player</c>, on purpose: this codebase already uses "player" heavily for audio
/// output (<see cref="Services.SoundboardPlayer"/>, <c>LocalMusicPlayer</c>, <c>IWavePlayerFactory</c>), so
/// the C# name avoids that collision — but the wire/route vocabulary is deliberately <em>"players"</em>
/// (<c>GET /party/list</c> returns <c>players</c>, ids appear in <c>/party/players/{id}/adjust</c> URLs).
/// </summary>
/// <remarks>
/// Counters are generic <c>{label, value, max, style}</c> so any system fits: the Daggerheart HP/Stress/Armor/
/// Hope loadout is a <em>UI-side preset</em>, never hardcoded here. System-wide stats that aren't tied to one
/// person (Daggerheart's Fear) live on the single-row <see cref="Data.PartyConfig"/> instead.
/// </remarks>
public class PartyMember
{
    /// <summary>Slug id (matched case-insensitively, NOCASE collation, like scenes/boards), used in
    /// <c>PUT/DELETE /party/players/{id}</c> and in hand-typed <c>/party/players/{id}/adjust</c> URLs.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Stored image file name of this member's portrait (uploaded via <c>/images</c>, resolved through
    /// <see cref="Services.ImageFileStorage"/>), or null. Never a path or URL — like <see cref="Board.BackgroundImage"/>.</summary>
    public string? Portrait { get; set; }

    /// <summary>Roster order: the panel and the TV render members ascending by this, ties broken by <see cref="Id"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>This member's tracked stats (one JSON column). See <see cref="PartyCounter"/>.</summary>
    public List<PartyCounter> Counters { get; set; } = [];
}

/// <summary>One tracked stat — <c>{label, value, max, style}</c>. A shared value object: it is owned by both
/// <see cref="PartyMember"/> (per-person stats) and <see cref="Data.PartyConfig"/> (table-level stats), the
/// same way <see cref="LightSettings"/> is owned by both <see cref="Scene"/> and
/// <see cref="Data.LightingConfig"/>.</summary>
public class PartyCounter
{
    /// <summary>Display name, e.g. "HP" — also the <b>adjust key</b> (matched case-insensitively by
    /// <c>/adjust?counter=</c>, so it must be unique within its owner).</summary>
    public string Label { get; set; } = "";

    public int Value { get; set; }

    /// <summary>Upper bound, or null for an unbounded counter. Adjust clamps <see cref="Value"/> into
    /// <c>[0, Max ?? 999]</c>.</summary>
    public int? Max { get; set; }

    /// <summary>How the renderer draws it: <c>null</c> | <c>"pips"</c> | <c>"number"</c>. Null lets the renderer
    /// decide (dots when <see cref="Max"/> is set, else a bare number); "pips" requires a small <see cref="Max"/>.</summary>
    public string? Style { get; set; }
}
