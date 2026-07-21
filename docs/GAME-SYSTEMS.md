# Game systems — architecture spec & contributor contract

> **Status**: all phases implemented (see [Phases](#phases--issue-slicing)). This document is self-contained on
> purpose: an agent or contributor adding a new system should need only this file plus the code it points at —
> no prior conversation context. Start at [Adding a new system](#adding-a-new-system-contributor-quick-guide).

Ambient Director's "game layer" (party tracker, bestiary, encounters, table counters, TV counter rendering)
started Daggerheart-flavoured. This spec makes the RPG **system pluggable** behind a well-defined C# contract
(`IGameSystem`), makes the active system **a table-level setting** (no system → the game layer hides from the
nav), and lets a system surface **top-bar quick controls** (Daggerheart: Fear −/+ visible from every page).

## Settled product decisions

These were decided with the project owner on 2026-07-21 — do not relitigate them in implementation:

1. **The contract is a C# interface (`IGameSystem`), not a JSON definition format.** Rationale: future systems
   may bring *behavior* (initiative order, dice mechanics, custom flows) that data files can't express, and the
   codebase's whole architecture is behavioral seams (`ILightService`, `IMusicSource`, `IImageSearchSource`).
2. **Community systems come in by pull request, not loadable plugin DLLs.** No `AssemblyLoadContext`, no
   runtime assembly scanning. "Add a class, register it, open a PR" is the bar.
3. **Upgrade path auto-stamps Daggerheart.** An existing install with any game data (players, bestiary
   enemies, encounters, or table counters) and no system chosen gets `daggerheart` stamped at startup, plus a
   one-shot semantic-key backfill. Nothing ever vanishes from the nav on update.
4. **"No system" hides only the Encounters nav tab** (the party tracker + bestiary live inside that tab, so
   they hide with it). The board editor keeps offering `party`/`enemies` elements, the TV keeps rendering
   them, and every API route stays fully functional — the gate is navigation-only.
5. **Built-ins: Daggerheart plus a deliberately simplistic D&D 5e sample** (`Dnd5eSystem`). The sample exists
   to prove the contract isn't Daggerheart-shaped and to be the copy-me template for contributors.

## Architecture overview

Two layers, because the panel is Blazor WASM and cannot call API-side C#:

```
API (server)                                        UI (Blazor WASM panel + key-free /tv page)
─────────────────────────────────────────────       ──────────────────────────────────────────
IGameSystem  (the contract; DI singletons)          GameSystemDto (wire mirror in Ui/Contracts)
  ├─ DaggerheartSystem                                ├─ Settings dropdown (pick system / none)
  └─ Dnd5eSystem (phase 3)                            ├─ Encounters-tab gate (MainLayout)
GameSystemRegistry (IEnumerable<IGameSystem>,         ├─ counter presets in editors (phase 3)
  lookup by id — the IImageSearchSource idiom)        └─ quickbar −/+ in QuickControls (phase 3)
PartyConfig.SystemId (SQLite, single row)
/systems endpoints (list + set current)             BoardCanvas renders what the render model says
TV render model resolves glyph/color/labels           (system-agnostic after phase 2)
  from the active system (phase 2)
```

**Behavior never crosses the wire.** Whatever the panel needs is display-shaped data serialized from the
active `IGameSystem` into `GameSystemDto`. A future behavioral capability (initiative, dice) is expressed as
API endpoints/state driven by the system implementation, with the panel again consuming plain data.

## The contract — `IGameSystem`

Lives in `src/AmbientDirector.Api/Services/Systems/IGameSystem.cs`. Implementations are DI singletons
registered in `Program.cs` (`builder.Services.AddSingleton<IGameSystem, DaggerheartSystem>();`) and discovered
via `GameSystemRegistry` (constructor-injected `IEnumerable<IGameSystem>` — the `IImageSearchSource` idiom).

```csharp
public interface IGameSystem
{
    /// Stable slug id ("daggerheart"). Stored in PartyConfig.SystemId; lowercase [a-z0-9-], unique.
    string Id { get; }

    /// i18n key for the display name ("system.daggerheart.name") — the house idiom: helpers return
    /// locale keys, never text (like Palette / LightFormat). Add the key to en.json AND pl.json.
    string NameKey { get; }

    /// Counter presets the panel offers on a party member ("Add <system> set" in the player editor).
    IReadOnlyList<CounterPreset> MemberCounters { get; }

    /// Counter presets for a bestiary enemy statblock (seeded on enemy create, offered in its editor).
    IReadOnlyList<CounterPreset> EnemyCounters { get; }

    /// Table-level counter presets (stats owned by no one player — Daggerheart's Fear). Seeded
    /// (adopt-or-append, never overwrite) into PartyConfig.Counters when the system is selected.
    IReadOnlyList<CounterPreset> TableCounters { get; }

    /// Keys of TableCounters to surface in the panel's always-visible top bar as −/+ quick controls.
    /// Default: none. Every key here MUST exist in TableCounters.
    IReadOnlyList<string> Quickbar => [];

    /// Literal chip text for the per-instance encounter highlight on the key-free TV ("SPOTLIGHT").
    /// A LITERAL, not a locale key — the TV page cannot reach /i18n (it sits behind the API key gate).
    /// Null hides the chip entirely.
    string? SpotlightLabel => null;
}

/// One counter preset. LabelKey is an i18n key resolved at apply time (panel: Localizer; server-side
/// seeding: LocaleService + the request's X-Ui-Lang). Key is the semantic id stamped onto the created
/// PartyCounter (see "Semantic counter keys"). Style/Max follow PartyValidation rules (pips ⇒ max 1–24).
/// Glyph is a curated name from GameSystemGlyphs.Known (null = plain dot); Color tints the dot / glyph.
public sealed record CounterPreset(
    string Key, string LabelKey, int Value = 0, int? Max = null,
    string? Style = null, string? Glyph = null, string? Color = null);
```

### Extension rules (how the contract grows without breaking anyone)

- **New optional members are added as default interface members** (`Quickbar` and `SpotlightLabel` already
  are). The codebase already relies on this C# feature (`ILightService.ApplyAsync`). An existing community
  system compiled against the old shape keeps working.
- **Big optional capabilities become marker interfaces**, not members: `IInitiativeSystem : IGameSystem`,
  `IDiceSystem : IGameSystem`. Consumers feature-detect (`if (registry.Current is IInitiativeSystem init)`)
  and light endpoints/UI up per capability. Nothing of this exists yet — this is the *anticipated* pattern.
- **`GameSystemRegistry` asserts invariants at construction** (unique ids, slug-shaped ids); the phase-3
  parametrized contract tests (`GameSystemContractTests`) extend that to preset validity for every registered
  system. A contributor whose system violates the contract finds out from `dotnet test`, not a code review.

### Glyphs — curated names, not raw SVG

`GameSystemGlyphs.Known` (same folder) is the allowed `CounterPreset.Glyph` vocabulary. Each name maps to a
hand-authored SVG + styling in `BoardCanvas.razor` (phase 2 moves the existing per-label mapping onto these
names). Current set:

| Glyph name      | Renders as                                            | Color behavior                   |
| --------------- | ----------------------------------------------------- | -------------------------------- |
| `heart-broken`  | broken heart (Daggerheart HP)                         | filled with `Color` (`#ff4d5e`)  |
| `heart-dark`    | near-black heart with light rim (Daggerheart Stress)  | self-styled, ignores `Color`     |
| `shield-broken` | broken shield (Daggerheart Armor)                     | filled with `Color` (`#cdd4e0`)  |
| `diamond`       | rotated square (Daggerheart Hope)                     | filled with `Color` (`#eef2f8`)  |
| *(null)*        | plain dot pip                                         | dot filled with `Color` (`--pip`)|

Adding a glyph = a small code PR (new name + SVG in the library); systems then reference it by name. Raw SVG
in the contract is deliberately rejected: definitions must not be able to ship malformed or hostile markup
onto the player-facing TV.

### Daggerheart reference data (must not drift from today's behavior)

| Scope  | Key      | LabelKey             | Value | Max | Style | Glyph           | Color     |
| ------ | -------- | -------------------- | ----- | --- | ----- | --------------- | --------- |
| member | `hp`     | `party.preset.hp`    | 0     | 6   | pips  | `heart-broken`  | `#ff4d5e` |
| member | `stress` | `party.preset.stress`| 0     | 6   | pips  | `heart-dark`    | *(null)*  |
| member | `armor`  | `party.preset.armor` | 0     | 3   | pips  | `shield-broken` | `#cdd4e0` |
| member | `hope`   | `party.preset.hope`  | 2     | 6   | pips  | `diamond`       | `#eef2f8` |
| enemy  | `hp`     | `party.preset.hp`    | 0     | 6   | pips  | `heart-broken`  | `#ff4d5e` |
| enemy  | `stress` | `party.preset.stress`| 0     | 3   | pips  | `heart-dark`    | *(null)*  |
| table  | `fear`   | `party.preset.fear`  | 0     | 12  | pips  | *(null)*        | `#ff4d2e` |

`Quickbar = ["fear"]`, `SpotlightLabel = "SPOTLIGHT"`. These numbers/colors are lifted from the pre-refactor
UI presets ([PartyEditor.razor](../src/AmbientDirector.Ui/Pages/PartyEditor.razor),
[Encounters.razor](../src/AmbientDirector.Ui/Pages/Encounters.razor)) and CSS
([_party.scss](../src/AmbientDirector.Ui/Styles/_party.scss)); a Daggerheart table's TV output must be
visually identical before and after each phase.

> **Fear on the TV (#144)**: the `fear` table counter's `Glyph` is null, so it draws as a plain dot pip in the
> panel's counter rows and the top-bar quickbar — that presentation is unchanged. But on **player-facing TV
> boards** Fear no longer rides the party element's counter strip: it now renders through the dedicated **`fear`
> board element**, a 12-slot **skull track** (art escalating from a plain skull at 1 to a demonic one at max),
> matched to the fear-**keyed** table counter. The dot-pip above still applies everywhere else (panel + quickbar).

### The D&D 5e sample

`Dnd5eSystem` ([Dnd5eSystem.cs](../src/AmbientDirector.Api/Services/Systems/Dnd5eSystem.cs)) is intentionally
minimal — it is documentation, not a full 5e implementation: members HP + AC (both `number` style, no max),
enemies HP, **no table counters, and it omits `Quickbar`/`SpotlightLabel` entirely** (inheriting the interface
defaults). Its emptiness is load-bearing: it proves every contract feature beyond `Id`/`NameKey`/the three
preset lists is optional, and it is the copy-me template in the contributor guide below.

## Semantic counter keys

`PartyCounter` (shared value object on members, enemies, encounter enemy instances, and `PartyConfig` table
counters) gains an **optional `Key`** (`string?`):

- **Why**: the counter `Label` doubles as the adjust key *and is localized* — the Fear preset creates "Fear"
  on an English panel but "Strach" on a Polish one. Nothing server-side can reliably find "the Fear counter"
  by label, and the TV's glyph theming used to match English labels only (Polish parties silently lost their
  themed glyphs). The `Key` is the stable, locale-independent semantic id (`"hp"`, `"fear"`).
- **Stamped by presets**, `null` on hand-added custom counters. Renaming a counter's label keeps its key.
- **Adjust resolution** (`PartyStore.AdjustInList` — used by `/party/players/{id}/adjust`,
  `/party/counters/adjust`, `/party/enemies/{id}/adjust`, `/encounters/{id}/enemies/{iid}/adjust`, and the
  matching AI ops): match by `Key` first (case-insensitive), then fall back to `Label` (case-insensitive).
  Stream Deck URLs should prefer keys: `/party/counters/adjust?counter=fear&delta=1` works in any language.
- **Validation** (`PartyValidation.ValidateCounters`): key optional; when present it is trimmed,
  lower-cased, must be slug-shaped (`[a-z0-9-_]`, ≤ 40 chars), and unique among the owner's non-null keys.
- **EF**: `PartyCounter` is an owned-JSON type — `Key` needs no schema change, but the model snapshot changes,
  so it rides the same migration as `PartyConfig.SystemId`.
- **UI round-trip warning**: the panel's `CounterEdit` form model (Ui/Contracts/PartyContracts.cs) MUST carry
  `Key` through `FromDto`/`ToDto`, or every manual save wipes the keys. Same for share export/import packs
  (they serialize the entities as-is, so `Key` rides along automatically — old packs without keys stay valid).

## System selection & persistence

- **Storage**: `PartyConfig.SystemId` (`string?`), single-row table, EF migration `PartyConfigSystemId`.
- **Tri-state sentinel**: `null` = *never chosen* (a fresh install, or pre-upgrade data — the startup
  auto-stamp below may set it); the literal `"none"` = *user explicitly chose no system* (never auto-stamped
  again); any other value = a system id. The wire only ever shows `null` (for both null/"none") or a valid id;
  the UI writes `"none"`, never null.
- **Selecting a system seeds its `TableCounters`** (adopt-or-append, never overwrite): for each preset, if a
  table counter with the same `Key` exists → skip; else if one exists whose `Label` equals the preset's
  localized label (current or English, case-insensitive) → **adopt it** (stamp the key, keep its value); else
  append a new counter (label localized server-side via `LocaleService` + `X-Ui-Lang`). Clearing the system
  deletes nothing.
- **Startup auto-stamp** (`Data/GameSystemUpgrade.cs`, run from Program.cs right after `LegacyImporter`, the
  same one-shot idiom): if `SystemId` is `null` **and** any game data exists (`PartyMembers`, `Enemies`,
  `Encounters` rows, or non-empty `PartyConfig.Counters`) → set `SystemId = "daggerheart"` and backfill
  `Key` on every counter (members, enemies, encounter enemy instances, table counters) whose label matches a
  known preset label in **English or Polish** (the two shipped locales at the time the presets existed):
  `hp|health→hp`, `stress|stres→stress`, `armor|armour|pancerz→armor`, `hope|nadzieja→hope`,
  `fear|strach→fear`. Idempotent; logs what it did; `"none"` is never touched.

## Wire surface

New endpoint group `/systems` (`Endpoints/SystemEndpoints.cs`), added to `IsProtectedPath` in Program.cs.
House patterns apply: nothing at the bare `/systems` path, no `GET /systems/{id}`, command endpoints accept
GET+POST (`EndpointHelpers.GetOrPost`).

- `GET /systems/list` → `GameSystemsDto { Systems: [GameSystemDto…], Current: string|null }` — every
  registered system's full definition (id, nameKey, the three preset lists, quickbar, spotlightLabel) plus
  the active id. One fetch serves the Settings dropdown, the nav gate, and (phase 3) the editors + quickbar.
- `GET|POST /systems/current?id=<id|none>` → validates the id against the registry (unknown →
  `ValidationException "error.system.unknown"`), stores it, seeds table counters (see above), bumps the TV
  rev via the shared party-touch helper when seeding changed counters, returns `{ current }`.

DTOs are duplicated per project **by design** (no shared contracts assembly). Adding/changing any of these
means touching BOTH `src/AmbientDirector.Api/Contracts/` and `src/AmbientDirector.Ui/Contracts/` by hand:
`GameSystemsDto`, `GameSystemDto`, `CounterPresetDto`, plus `PartyCounter`/`PartyCounterDto` (`Key`) and
`PartyDto` (gains `System` — the active id — so `/party/list` consumers and both AI surfaces' `list_party`
learn the table's idiom with **no new AI ops**; system *selection* stays deliberately excluded from the AI
façade, like all setup).

## UI behavior

- **Nav gate** (`MainLayout.razor`): the `encounters` tab renders only when a system is active. State lives
  in `UiState` (`GameSystem` + `SetGameSystem`, the `AssistantConfigured` idiom), loaded once at layout init
  from `/systems/list` and updated live when Settings changes it (tab appears/disappears without reload).
  Deep links to `/encounters`/`/party/*` still work when hidden — the gate is nav-only.
- **Settings → General**: a "Game system" section (`SectionHeading` + panel + `<select>`, mirroring the
  Language section) listing None + each system by localized `NameKey`; saving calls `/systems/current`,
  toasts, and updates `UiState`.
- **Presets from the system** (done, #129): the player editor's "Add Daggerheart set" button becomes "Add
  {system name} set" driven by the active system's `MemberCounters`; the enemy editor + the Encounters
  page's create-enemy seed use `EnemyCounters`; the table-counter editor's "Add Fear" becomes per-preset
  add buttons from `TableCounters`. No system → only the generic "Add counter". Applying a preset stamps
  `Key` and skips existing keys/labels (case-insensitive) so re-clicking never duplicates. The shared apply
  logic is `CounterPresets` (Ui/Contracts/PartyContracts.cs).
- **Quickbar** (done, #129): `QuickControls.razor` renders, for each `Quickbar` key present in the table
  counters, a compact chip — label, value, − and + buttons (calling `/party/counters/adjust?counter=<key>`),
  visible on **all** viewport sizes (unlike the secondary music buttons — mid-session reachability is the
  point). Its existing 5 s poll adds `/party/list` (or the value from the adjust response) only while a
  system with a non-empty quickbar is active.

## Render pipeline (phase 2) — implemented (#128)

Before phase 2, `BoardCanvas.razor` decided glyphs by **English label matching** and hardcoded Fear's red + the
`SPOTLIGHT` literal. Phase 2 makes the render model carry presentation, resolved server-side from the active
system, so BoardCanvas is system-agnostic:

- `TvPartyCounterDto` gains `Glyph` + `Color` (nullable). The API resolves them when inlining the render
  model (`/tv/state` for boards and encounters): counter `Key` → active system's preset (member/enemy/table
  scope respectively) → its `Glyph`/`Color`. No key or no match → null/null (plain neutral dot).
- `TvEnemyDto`/the encounter render model carries `SpotlightLabel` (from the active system; null hides the
  chip) instead of the hardcoded literal.
- `BoardCanvas.razor` drops `PipShape`/`PipColor` label matching; keeps the glyph SVG library keyed by the
  curated glyph names; CSS classes move from track names (`bparty__pip--hp`) to glyph names
  (`bparty__pip--heart-broken` etc.), preserving today's exact visuals for Daggerheart.
- The panel's client-built render models (`PartyRender.ToRenderModel` + `BoardRender`, used by the editor
  preview, list cards, remote rail) resolve the same way from the `GameSystemDto` the panel already has.
- **Enemy counters hide their max on the TV** (#129 follow-up): in `BoardCanvas`'s enemies element an enemy's
  pip counter draws only the *filled* marks (never the empty remainder) and a number counter shows the bare
  value (never `value/max`) — so players watch damage accrue but never learn how many marks are left before the
  enemy drops. Player and table counters still render the full track; the GM's own Encounters tracker is
  unaffected. This is a player-facing render rule only — the render model still carries `max`.
- **Acceptance**: an English Daggerheart table renders pixel-identically; a **Polish** Daggerheart table
  gains the themed glyphs it silently lacked (label matching never hit "Stres"/"Pancerz"/"Nadzieja") — via
  the upgrade backfill's keys.

## Phases / issue slicing

Each phase is one PR that keeps `dotnet build` + `dotnet test` green and changes no behavior beyond its scope.

1. **Contract + selection + keys** — ✅ done (#127). `IGameSystem`/`CounterPreset`/`GameSystemGlyphs`/`GameSystemRegistry`,
   `DaggerheartSystem`, `PartyConfig.SystemId` + migration, `GameSystemUpgrade` auto-stamp + key backfill,
   `PartyCounter.Key` (+ validation + adjust-by-key + UI mirrors incl. `CounterEdit`), `/systems` endpoints
   (+ `IsProtectedPath`), Settings section, Encounters-tab gate, `PartyDto.System`, locale keys (en+pl),
   tests, CLAUDE.md + this spec. **Behavioral change**: only the gate + the new setting; presets/rendering
   untouched.
2. **Server-resolved render presentation** — ✅ done (#128). Render DTO `Glyph`/`Color` (`TvPartyCounterDto`) +
   `SpotlightLabel` (`TvEnemyDto`), API resolution in `TvEndpoints` (counter `Key` → active system's preset per
   scope) + panel-side resolution in `PartyRender` (via `GameSystemsDto.Active`, fetched by the Boards/BoardEditor/
   TvRemote pages), `BoardCanvas` keyed by glyph names, `_party.scss` classes renamed to glyph names. Visual
   no-op for English Daggerheart; fixes the long-standing Polish-glyph bug. No new locale keys.
3. **Presets from the system + quickbar + the sample** — ✅ done (#129). Editors/create flows read the active
   `GameSystemDto` (player/enemy "Add {system} set" via `CounterPresets`, per-preset table-counter add buttons,
   the create-enemy seed), `QuickControls` quickbar −/+ chips (visible on every viewport), `Dnd5eSystem`,
   `GameSystemContractTests` (parametrized over every registered system), and the contributor guide below
   finalized. New locale keys `partyEditor.addSet` / `party.addPreset` / `party.preset.ac` / `system.dnd5e.name`
   (en+pl); the dead `partyEditor.addDaggerheart` / `party.preset.addFear` removed.

## Adding a new system (contributor quick guide)

1. Copy `src/AmbientDirector.Api/Services/Systems/Dnd5eSystem.cs` (the deliberately minimal sample) to
   `YourSystem.cs`; give it a unique slug `Id` and fill the preset lists. Every member is optional except
   `Id`, `NameKey`, and the three preset lists (which may be empty) — `Dnd5eSystem` omits `Quickbar` and
   `SpotlightLabel` to show that.
2. Add your `NameKey` + preset `LabelKey`s to `src/AmbientDirector.Api/Locales/en.json` (required) and
   `pl.json` (ideally — English is the fallback).
3. Register it in `Program.cs`: `builder.Services.AddSingleton<IGameSystem, YourSystem>();`
4. Run `dotnet test` — `GameSystemContractTests` validates your ids, keys, styles, glyph names, quickbar
   references and locale keys. Fix what it reports.
5. Open a PR. If your system needs a glyph that doesn't exist, add it to the curated library in the same PR
   (see "Glyphs" above) — do not embed raw SVG in presets.

## Invariants & pitfalls checklist

- DTOs are **duplicated by hand** (API `Contracts/` ↔ UI `Contracts/`); every wire change touches both.
- Any entity/`OnModelCreating` change needs an EF migration (`dotnet dotnet-ef migrations add <Name> -o
  Data/Migrations` from the API project) — the app only applies migrations.
- New user-facing failure = code-carrying `ValidationException`/`NotConfiguredException` + `error.*` key in
  **both** `en.json` and `pl.json` (see the error-localization notes in CLAUDE.md).
- New protected route prefixes go into `IsProtectedPath` (Program.cs). `/systems` is protected; the key-free
  TV surface must never need it (that's why `SpotlightLabel` is a literal and render presentation is inlined).
- The Encounters tab gate is **navigation-only**. Never 4xx the game endpoints because no system is set.
- `"none"` (explicit no-system) must never be auto-stamped back to `daggerheart`.
- Counter `Label` remains a valid adjust token forever (Stream Deck back-compat); `Key` only adds a better one.
- `CounterEdit` (panel form model) must round-trip `Key`, or manual saves silently strip semantics.
- A Daggerheart table's TV rendering must look identical after every phase (the reference table above is the
  source of truth for values/colors).
