namespace AmbientDirector.Api.Contracts;

// GET /systems/list — every registered game system's full display-shaped definition plus the active id
// (issue #127; design spec: docs/GAME-SYSTEMS.md). One fetch serves the Settings dropdown, the panel's
// Encounters-tab gate, and (phase 3, #129) the editors' presets + the top-bar quickbar. Current is null for
// both "never chosen" and the explicit "none" sentinel — the wire never shows the raw tri-state. The panel
// mirrors these shapes by hand in AmbientDirector.Ui/Contracts/SystemContracts.cs — keep the two in sync.
public record GameSystemsDto(List<GameSystemDto> Systems, string? Current);

// The display-shaped projection of one IGameSystem. Behavior never crosses the wire — a future behavioral
// capability (initiative, dice) becomes API endpoints, not DTO fields here. NameKey/LabelKey are i18n keys
// the panel resolves via its Localizer; SpotlightLabel is a LITERAL for the key-free TV (see the contract).
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
