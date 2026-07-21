namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's PartyMember/Enemy/PartyCounter shapes (Models/PartyMember.cs, Models/Enemy.cs) — contracts
// are duplicated per project by design, so keep the two in sync by hand when a field changes. The wire
// vocabulary is deliberately "players" (GET /party/list returns players; ids appear in /party/players/{id}
// urls) even though the API's C# type is PartyMember (renamed there to avoid colliding with the audio
// "player"). A counter is a generic {label, value, max, style}: style is null | "pips" | "number" — null lets
// the renderer decide (dots when a max is set, else a bare number); "pips" needs a small max (1–24).
// Table-level counters (e.g. Fear) and the encounter's enemy roster (issue #120) live alongside the players in
// the same envelope, plus System — the active game system's id (issue #127; null = none chosen), which gates
// the Encounters tab and (phase 3, #129) drives the editors' presets. Counter presets come from the active
// system's definition (GET /systems/list) — the API stores only generic counters.
public record PartyDto(List<PartyPlayerDto> Players, List<PartyCounterDto> Counters, List<PartyEnemyDto> Enemies, string? System);

public record PartyPlayerDto(string Id, string Name, string? Portrait, int SortOrder, List<PartyCounterDto>? Counters);

// An enemy is a reusable bestiary statblock (issue #122): name, portrait and base counter definitions. Base
// stats only — the per-fight spotlight (boss) flag and live values live on an encounter instance, not here.
public record PartyEnemyDto(string Id, string Name, string? Portrait, int SortOrder, List<PartyCounterDto>? Counters);

// Key is the optional stable semantic id (issue #127): stamped by system presets ("hp", "fear"), null on
// custom counters. It is the preferred /adjust token (labels are localized text) and, from phase 2 (#128),
// the render pipeline's glyph key — so editors MUST round-trip it (see CounterEdit).
public record PartyCounterDto(string Label, int Value, int? Max, string? Style, string? Key = null);

// Mutable form model for editing one player in the panel; converts to the immutable wire DTO on save (the
// BoardEdit pattern). Each counter needs per-field editing (label/value/max/style), so it gets its own mutable
// CounterEdit.
public class PlayerEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Portrait { get; set; }
    public int SortOrder { get; set; }
    public List<CounterEdit> Counters { get; set; } = [];

    public static PlayerEdit FromDto(PartyPlayerDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Portrait = dto.Portrait,
        SortOrder = dto.SortOrder,
        Counters = [.. (dto.Counters ?? []).Select(CounterEdit.FromDto)],
    };

    public PartyPlayerDto ToDto() => new(Id, Name.Trim(), Portrait, SortOrder, [.. Counters.Select(c => c.ToDto())]);
}

// Mutable form model for editing one bestiary enemy template in the panel; the PlayerEdit twin (name, portrait,
// order, base counter definitions). Converts to the immutable wire DTO on save.
public class EnemyEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Portrait { get; set; }
    public int SortOrder { get; set; }
    public List<CounterEdit> Counters { get; set; } = [];

    public static EnemyEdit FromDto(PartyEnemyDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Portrait = dto.Portrait,
        SortOrder = dto.SortOrder,
        Counters = [.. (dto.Counters ?? []).Select(CounterEdit.FromDto)],
    };

    public PartyEnemyDto ToDto() => new(Id, Name.Trim(), Portrait, SortOrder, [.. Counters.Select(c => c.ToDto())]);
}

public class CounterEdit
{
    public string Label { get; set; } = "";
    public int Value { get; set; }
    public int? Max { get; set; } = 6;

    // The editor always commits an explicit style, and the Segmented control needs a non-null value — so a null
    // style off the wire (the renderer decides) is surfaced as "pips" when it has a small max, else "number".
    // New counters default to pips/max 6 (see the pages' AddCounter).
    public string Style { get; set; } = "pips";

    // The semantic key rides along invisibly (no editor field): dropping it here would strip every counter's
    // key on the next manual save. Renaming the label keeps the key — that is the point of having one.
    public string? Key { get; set; }

    public static CounterEdit FromDto(PartyCounterDto dto) => new()
    {
        Label = dto.Label,
        Value = dto.Value,
        Max = dto.Max,
        Style = dto.Style ?? (dto.Max is >= 1 and <= 24 ? "pips" : "number"),
        Key = dto.Key,
    };

    // Materialize a game-system preset (issue #129) into an editor row: stamp the semantic Key and use the
    // already-localized label, applying the same null-style fallback as FromDto (dots for a small max, else a
    // number). The style is normally set on the preset, so the fallback only guards a preset that leaves it null.
    public static CounterEdit FromPreset(CounterPresetDto preset, string label) => new()
    {
        Key = preset.Key,
        Label = label,
        Value = preset.Value,
        Max = preset.Max,
        Style = preset.Style ?? (preset.Max is >= 1 and <= 24 ? "pips" : "number"),
    };

    public PartyCounterDto ToDto() => new(Label.Trim(), Value, Max, Style, Key);
}

// Applies an active game system's counter presets to an editor's counter list — the player/enemy "Add {system}
// set" buttons, the table-counter per-preset add buttons, and the Encounters create-enemy seed (issue #129).
// Each preset stamps its semantic Key and its localized label; a preset whose Key OR localized Label already
// exists (case-insensitive) is skipped, so re-clicking a set/preset button never duplicates a counter.
public static class CounterPresets
{
    public static void Apply(List<CounterEdit> target, IEnumerable<CounterPresetDto> presets, Func<string, string> localize)
    {
        foreach (var preset in presets)
        {
            AddOne(target, preset, localize);
        }
    }

    public static void AddOne(List<CounterEdit> target, CounterPresetDto preset, Func<string, string> localize)
    {
        var label = localize(preset.LabelKey);
        var duplicate = target.Any(c =>
            (!string.IsNullOrEmpty(preset.Key) && string.Equals(c.Key, preset.Key, StringComparison.OrdinalIgnoreCase))
            || string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            return;
        }
        target.Add(CounterEdit.FromPreset(preset, label));
    }
}

// Builds the same TvPartyDto shape the TV gets inline, from the panel's own /party/list data — so the ONE
// BoardCanvas renderer draws a party element identically in the editor preview, list cards and remote rail.
// Portrait file names are mapped through Api.ImageUrl (key-bearing /images/{name} urls), exactly like
// BoardRender maps board images; pass Api.ImageUrl as imageUrl. The active game system (issue #128; from
// /systems/list — GameSystemsDto.Active) resolves each counter's glyph/colour + the spotlight label EXACTLY as
// the API does server-side, so a panel preview matches the TV; null system → neutral dots, no chip.
public static class PartyRender
{
    public static TvPartyDto ToRenderModel(PartyDto party, Func<string?, string?> imageUrl, GameSystemDto? system = null) =>
        new(
            [.. party.Players.Select(p => new TvPartyPlayerDto(
                p.Name,
                imageUrl(p.Portrait),
                [.. (p.Counters ?? []).Select(c => ToRenderCounter(c, system?.MemberCounters))]))],
            [.. party.Counters.Select(c => ToRenderCounter(c, system?.TableCounters))],
            // Bestiary templates rendered on a legacy board's enemies element: portrait + tracks, no per-instance
            // spotlight (that lives on an encounter instance, not the template — so always false here; the label
            // still rides along to match the API's enemy projection).
            [.. party.Enemies.Select(e => new TvEnemyDto(
                e.Name,
                imageUrl(e.Portrait),
                false,
                [.. (e.Counters ?? []).Select(c => ToRenderCounter(c, system?.EnemyCounters))],
                system?.SpotlightLabel))]);

    // Resolve one counter's glyph/colour: its semantic Key → the matching preset in the given scope of the
    // active system → the preset's Glyph + Color. No presets, no key, or no match → both null (neutral dot).
    private static TvPartyCounterDto ToRenderCounter(PartyCounterDto c, List<CounterPresetDto>? presets)
    {
        var preset = presets is null || string.IsNullOrEmpty(c.Key)
            ? null
            : presets.FirstOrDefault(p => string.Equals(p.Key, c.Key, StringComparison.OrdinalIgnoreCase));
        return new TvPartyCounterDto(c.Label, c.Value, c.Max, c.Style, preset?.Glyph, preset?.Color);
    }
}
