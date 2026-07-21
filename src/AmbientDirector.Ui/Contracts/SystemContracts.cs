namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's GET /systems/list shapes (Contracts/SystemContracts.cs) — contracts are duplicated per
// project by design, keep the two in sync by hand. One fetch serves the Settings dropdown, the layout's
// Encounters-tab gate, and (phase 3, #129) the editors' presets + the top-bar quickbar. Current is the active
// system's id or null (none chosen / explicitly cleared — the wire never shows the stored tri-state).
// NameKey/LabelKey are i18n keys resolved through the Localizer; SpotlightLabel is a literal for the key-free
// TV (never localized).
public record GameSystemsDto(List<GameSystemDto> Systems, string? Current);

public record GameSystemDto(
    string Id,
    string NameKey,
    List<CounterPresetDto> MemberCounters,
    List<CounterPresetDto> EnemyCounters,
    List<CounterPresetDto> TableCounters,
    List<string> Quickbar,
    string? SpotlightLabel);

public record CounterPresetDto(
    string Key, string LabelKey, int Value, int? Max, string? Style, string? Glyph, string? Color);
