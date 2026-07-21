namespace AmbientDirector.Api.Services.Systems;

/// <summary>
/// The pluggable RPG game-system contract (issue #127; design spec: <c>docs/GAME-SYSTEMS.md</c>). A system
/// describes what the generic party/bestiary/encounter layer should offer at *this* table: counter presets
/// for members / enemies / the table, which table counters the panel's top bar surfaces as −/+ quick
/// controls, and flavour for the player-facing TV. Implementations are data-only DI singletons registered in
/// Program.cs and discovered through <see cref="GameSystemRegistry"/> (the <c>IImageSearchSource</c> idiom);
/// community systems are added by PR — a class, its locale keys, one registration line — never by loadable
/// plugin assemblies.
/// </summary>
/// <remarks>
/// Contract growth rules: new optional members are added as <b>default interface members</b> (like
/// <c>ILightService.ApplyAsync</c>) so existing implementations never break; large optional capabilities
/// (initiative order, dice mechanics) become <b>marker interfaces</b> (<c>IInitiativeSystem : IGameSystem</c>)
/// that consumers feature-detect — they must never become required members here.
/// </remarks>
public interface IGameSystem
{
    /// <summary>Stable slug id ("daggerheart") — lowercase <c>[a-z0-9-_]</c>, unique across registered
    /// systems (asserted by <see cref="GameSystemRegistry"/>). Stored in <c>PartyConfig.SystemId</c>.</summary>
    string Id { get; }

    /// <summary>i18n key of the display name ("system.daggerheart.name") — the house idiom (like
    /// <c>Palette</c>/<c>LightFormat</c>): helpers return locale keys, never text. Add the key to
    /// <c>en.json</c> (required) and <c>pl.json</c>.</summary>
    string NameKey { get; }

    /// <summary>Counter presets offered on a party member ("Add {system} set" in the player editor).</summary>
    IReadOnlyList<CounterPreset> MemberCounters { get; }

    /// <summary>Counter presets for a bestiary enemy statblock (seeded on enemy create, offered in its editor).</summary>
    IReadOnlyList<CounterPreset> EnemyCounters { get; }

    /// <summary>Table-level counter presets — stats owned by no one player (Daggerheart's Fear). Seeded
    /// adopt-or-append into <c>PartyConfig.Counters</c> when the system is selected (see SystemEndpoints);
    /// never overwrites an existing counter's value.</summary>
    IReadOnlyList<CounterPreset> TableCounters { get; }

    /// <summary>Keys of <see cref="TableCounters"/> the panel surfaces in the always-visible top bar as −/+
    /// quick controls. Every entry must reference a <see cref="TableCounters"/> key. Default: none.</summary>
    IReadOnlyList<string> Quickbar => [];

    /// <summary>Literal chip text for the per-instance encounter highlight on the player-facing TV
    /// ("SPOTLIGHT"). A LITERAL, not a locale key: the TV page is key-free and cannot reach <c>/i18n</c>
    /// (which sits behind the API-key gate). Null hides the chip.</summary>
    string? SpotlightLabel => null;
}

/// <summary>One counter preset of an <see cref="IGameSystem"/>. Applying it creates a
/// <see cref="Models.PartyCounter"/> with <see cref="Key"/> stamped as the stable, locale-independent
/// semantic id and the label resolved from <see cref="LabelKey"/> at apply time (panel: <c>Localizer</c>;
/// server-side table seeding: <c>LocaleService</c> + the request's <c>X-Ui-Lang</c>).</summary>
/// <param name="Key">Semantic id ("hp", "fear") — lowercase slug, unique within its preset list.</param>
/// <param name="LabelKey">i18n key of the display label ("party.preset.hp"), present in <c>en.json</c>.</param>
/// <param name="Value">Starting value.</param>
/// <param name="Max">Upper bound, or null for unbounded. Required and 1–24 when <paramref name="Style"/> is "pips".</param>
/// <param name="Style">null | "pips" | "number" — the <see cref="Models.PartyCounter.Style"/> rules.</param>
/// <param name="Glyph">Curated themed-pip name from <see cref="GameSystemGlyphs.Known"/>, or null for a
/// plain dot. Never raw SVG — presets must not be able to ship markup onto the key-free TV.</param>
/// <param name="Color">Content colour (hex) tinting the dot / glyph fill where the glyph supports it, or
/// null for the glyph's self-styled default. Consumed by the render pipeline in phase 2 (#128).</param>
public sealed record CounterPreset(
    string Key,
    string LabelKey,
    int Value = 0,
    int? Max = null,
    string? Style = null,
    string? Glyph = null,
    string? Color = null);

/// <summary>The curated vocabulary for <see cref="CounterPreset.Glyph"/>. Each name maps to a hand-authored
/// SVG + styling in <c>BoardCanvas.razor</c> (keyed by these names from phase 2, #128, on). Adding a glyph is
/// a small code PR (name here + SVG there); systems then reference it by name.</summary>
public static class GameSystemGlyphs
{
    /// <summary>Broken heart (Daggerheart HP) — filled with the preset's colour.</summary>
    public const string HeartBroken = "heart-broken";

    /// <summary>Near-black heart with a light rim (Daggerheart Stress) — self-styled, ignores colour.</summary>
    public const string HeartDark = "heart-dark";

    /// <summary>Broken shield (Daggerheart Armor) — filled with the preset's colour.</summary>
    public const string ShieldBroken = "shield-broken";

    /// <summary>Rotated square (Daggerheart Hope) — filled with the preset's colour.</summary>
    public const string Diamond = "diamond";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        HeartBroken, HeartDark, ShieldBroken, Diamond,
    };
}
