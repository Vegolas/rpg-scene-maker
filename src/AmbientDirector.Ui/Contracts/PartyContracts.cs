namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's PartyMember/Enemy/PartyCounter shapes (Models/PartyMember.cs, Models/Enemy.cs) — contracts
// are duplicated per project by design, so keep the two in sync by hand when a field changes. The wire
// vocabulary is deliberately "players" (GET /party/list returns players; ids appear in /party/players/{id}
// urls) even though the API's C# type is PartyMember (renamed there to avoid colliding with the audio
// "player"). A counter is a generic {label, value, max, style}: style is null | "pips" | "number" — null lets
// the renderer decide (dots when a max is set, else a bare number); "pips" needs a small max (1–24).
// Table-level counters (e.g. Fear) and the encounter's enemy roster (issue #120) live alongside the players in
// the same envelope. The Daggerheart/Fear presets are a UI-only convenience — the API stores only generic counters.
public record PartyDto(List<PartyPlayerDto> Players, List<PartyCounterDto> Counters, List<PartyEnemyDto> Enemies);

public record PartyPlayerDto(string Id, string Name, string? Portrait, int SortOrder, List<PartyCounterDto>? Counters);

// An enemy is a member's twin minus the portrait (v1 enemy cards are text + tracks), plus a Spotlight (boss)
// flag the TV renders red. Same generic counters as a player.
public record PartyEnemyDto(string Id, string Name, bool Spotlight, int SortOrder, List<PartyCounterDto>? Counters);

public record PartyCounterDto(string Label, int Value, int? Max, string? Style);

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

// Mutable form model for editing one enemy in the panel; the PlayerEdit twin minus the portrait, plus the
// Spotlight flag. Converts to the immutable wire DTO on save.
public class EnemyEdit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Spotlight { get; set; }
    public int SortOrder { get; set; }
    public List<CounterEdit> Counters { get; set; } = [];

    public static EnemyEdit FromDto(PartyEnemyDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Spotlight = dto.Spotlight,
        SortOrder = dto.SortOrder,
        Counters = [.. (dto.Counters ?? []).Select(CounterEdit.FromDto)],
    };

    public PartyEnemyDto ToDto() => new(Id, Name.Trim(), Spotlight, SortOrder, [.. Counters.Select(c => c.ToDto())]);
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

    public static CounterEdit FromDto(PartyCounterDto dto) => new()
    {
        Label = dto.Label,
        Value = dto.Value,
        Max = dto.Max,
        Style = dto.Style ?? (dto.Max is >= 1 and <= 24 ? "pips" : "number"),
    };

    public PartyCounterDto ToDto() => new(Label.Trim(), Value, Max, Style);
}

// Builds the same TvPartyDto shape the TV gets inline, from the panel's own /party/list data — so the ONE
// BoardCanvas renderer draws a party element identically in the editor preview, list cards and remote rail.
// Portrait file names are mapped through Api.ImageUrl (key-bearing /images/{name} urls), exactly like
// BoardRender maps board images; pass Api.ImageUrl as imageUrl.
public static class PartyRender
{
    public static TvPartyDto ToRenderModel(PartyDto party, Func<string?, string?> imageUrl) =>
        new(
            [.. party.Players.Select(p => new TvPartyPlayerDto(
                p.Name,
                imageUrl(p.Portrait),
                [.. (p.Counters ?? []).Select(ToRenderCounter)]))],
            [.. party.Counters.Select(ToRenderCounter)],
            // Enemies carry no portrait — text + tracks + the spotlight flag.
            [.. party.Enemies.Select(e => new TvEnemyDto(
                e.Name,
                e.Spotlight,
                [.. (e.Counters ?? []).Select(ToRenderCounter)]))]);

    private static TvPartyCounterDto ToRenderCounter(PartyCounterDto c) => new(c.Label, c.Value, c.Max, c.Style);
}
