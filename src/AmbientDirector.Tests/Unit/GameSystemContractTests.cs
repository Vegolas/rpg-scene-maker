using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Systems;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>
/// The per-system contract every registered <see cref="IGameSystem"/> must satisfy (issue #129; design spec:
/// docs/GAME-SYSTEMS.md § "Extension rules" + "Adding a new system"). Parametrized over ALL IGameSystem
/// implementations in the API assembly (discovered by reflection), so a contributor's new system is validated
/// automatically — a bad glyph name, a dangling quickbar key, a duplicate preset key, an out-of-range pips max
/// or a missing locale key fails <c>dotnet test</c> with a message that names the offending system, not a code
/// review. The cross-system id invariants (unique, slug) are the registry's job and are pinned separately in
/// <see cref="GameSystemRegistryTests"/>; the whole registered set is re-checked here in one fact too.
/// </summary>
public class GameSystemContractTests
{
    // Every concrete IGameSystem in the API assembly. They are data-only singletons with an implicit
    // parameterless ctor, so Activator can build them; the theory takes the Type (xUnit-serializable, and it
    // keeps the case names readable: "…(systemType: typeof(DaggerheartSystem))").
    public static IEnumerable<object[]> SystemTypes() =>
        typeof(IGameSystem).Assembly.GetTypes()
            .Where(t => typeof(IGameSystem).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .Select(t => new object[] { t });

    // The canonical shipped key set (embedded in the API assembly, no on-disk dependency) — a system's NameKey
    // and every preset LabelKey must exist here (English is required; pl.json is the ideal but falls back).
    private static readonly IReadOnlyDictionary<string, string> EnStrings = LocaleService.ShippedStrings("en");
    private static readonly HashSet<string> Styles = new(StringComparer.Ordinal) { "pips", "number" };

    [Theory]
    [MemberData(nameof(SystemTypes))]
    public void Id_is_a_lowercase_slug_and_not_reserved(Type systemType)
    {
        var system = Instantiate(systemType);
        Assert.False(string.IsNullOrWhiteSpace(system.Id), $"{systemType.Name}: empty id");
        Assert.True(LightValidation.IsSlug(system.Id), $"{systemType.Name}: id '{system.Id}' is not a slug");
        Assert.Equal(system.Id.ToLowerInvariant(), system.Id);
        Assert.NotEqual(GameSystemRegistry.None, system.Id); // the reserved "explicitly no system" sentinel
    }

    [Theory]
    [MemberData(nameof(SystemTypes))]
    public void NameKey_ships_in_english(Type systemType)
    {
        var system = Instantiate(systemType);
        Assert.True(EnStrings.ContainsKey(system.NameKey),
            $"{systemType.Name}: NameKey '{system.NameKey}' is missing from the embedded en.json");
    }

    [Theory]
    [MemberData(nameof(SystemTypes))]
    public void Member_enemy_and_table_presets_are_valid(Type systemType)
    {
        var system = Instantiate(systemType);
        AssertPresets(systemType, "member", system.MemberCounters);
        AssertPresets(systemType, "enemy", system.EnemyCounters);
        AssertPresets(systemType, "table", system.TableCounters);
    }

    [Theory]
    [MemberData(nameof(SystemTypes))]
    public void Quickbar_keys_reference_table_counters(Type systemType)
    {
        var system = Instantiate(systemType);
        var tableKeys = system.TableCounters.Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(system.Quickbar, key => Assert.True(tableKeys.Contains(key),
            $"{systemType.Name}: quickbar key '{key}' has no matching table counter"));
    }

    // The whole registered set constructs a valid registry (the app-startup invariant) and includes the two
    // built-ins — the sample's presence is what proves the contract isn't Daggerheart-only.
    [Fact]
    public void All_systems_form_a_valid_registry_with_both_builtins()
    {
        var systems = SystemTypes().Select(row => Instantiate((Type)row[0])).ToArray();
        var registry = new GameSystemRegistry(systems);
        Assert.Equal(systems.Length, registry.All.Count);
        Assert.Contains(registry.All, s => s is DaggerheartSystem);
        Assert.Contains(registry.All, s => s is Dnd5eSystem);
    }

    private static void AssertPresets(Type systemType, string scope, IReadOnlyList<CounterPreset> presets)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in presets)
        {
            var where = $"{systemType.Name}/{scope} preset '{p.Key}'";

            // Key: present, a lowercase slug, unique within this scope (it is the render/adjust semantic id).
            Assert.False(string.IsNullOrWhiteSpace(p.Key), $"{systemType.Name}/{scope}: a preset has an empty key");
            Assert.True(LightValidation.IsSlug(p.Key) && p.Key == p.Key.ToLowerInvariant(),
                $"{where}: key is not a lowercase slug");
            Assert.True(keys.Add(p.Key), $"{where}: duplicate key within the {scope} scope");

            // LabelKey resolvable (English is the canonical, required set).
            Assert.True(EnStrings.ContainsKey(p.LabelKey),
                $"{where}: LabelKey '{p.LabelKey}' is missing from the embedded en.json");

            // Style / max / pips: the same rules PartyValidation enforces on a stored counter, so a seeded or
            // applied preset can never produce what PUT /party/counters would reject.
            if (p.Style is not null)
                Assert.True(Styles.Contains(p.Style), $"{where}: invalid style '{p.Style}'");
            if (p.Max is { } max)
                Assert.InRange(max, 1, 999);
            if (p.Style == "pips")
                Assert.True(p.Max is >= 1 and <= 24, $"{where}: pips style needs a max of 1–24");

            // Glyph: only a curated name may reach the key-free TV (raw SVG is rejected by the contract).
            if (p.Glyph is not null)
                Assert.True(GameSystemGlyphs.Known.Contains(p.Glyph), $"{where}: unknown glyph '{p.Glyph}'");
        }
    }

    private static IGameSystem Instantiate(Type systemType) => (IGameSystem)Activator.CreateInstance(systemType)!;
}
