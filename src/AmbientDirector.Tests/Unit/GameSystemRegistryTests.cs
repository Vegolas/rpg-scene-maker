using AmbientDirector.Api.Services.Systems;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>The game-system registry's startup invariants + lookup rules (issue #127). The full per-system
/// preset contract (glyph names, quickbar references, locale keys) is phase 3's GameSystemContractTests
/// (#129); here we pin what the registry itself must reject and how the stored tri-state resolves.</summary>
public class GameSystemRegistryTests
{
    private sealed class FakeSystem(string id) : IGameSystem
    {
        public string Id => id;
        public string NameKey => $"system.{id}.name";
        public IReadOnlyList<CounterPreset> MemberCounters => [];
        public IReadOnlyList<CounterPreset> EnemyCounters => [];
        public IReadOnlyList<CounterPreset> TableCounters => [];
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Daggerheart")] // uppercase — ids are stored/compared as lowercase slugs
    [InlineData("bad id")]
    [InlineData("none")]        // reserved sentinel for "explicitly no system"
    public void Invalid_or_reserved_ids_fail_at_construction(string id)
    {
        Assert.Throws<InvalidOperationException>(() => new GameSystemRegistry([new FakeSystem(id)]));
    }

    [Fact]
    public void Duplicate_ids_fail_at_construction()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new GameSystemRegistry([new FakeSystem("dupe"), new FakeSystem("dupe")]));
    }

    [Fact]
    public void Find_resolves_case_insensitively_and_maps_the_tri_state_to_null()
    {
        var registry = new GameSystemRegistry([new DaggerheartSystem()]);

        Assert.Equal("daggerheart", registry.Find("DAGGERHEART")?.Id);
        Assert.Null(registry.Find(null));                 // never chosen
        Assert.Null(registry.Find(GameSystemRegistry.None)); // explicit "none"
        Assert.Null(registry.Find("removed-community-system"));
    }

    [Fact]
    public void Daggerheart_quickbar_references_its_own_table_counters()
    {
        var daggerheart = new DaggerheartSystem();
        var tableKeys = daggerheart.TableCounters.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        Assert.All(daggerheart.Quickbar, key => Assert.Contains(key, tableKeys));
        Assert.All(
            new[] { daggerheart.MemberCounters, daggerheart.EnemyCounters, daggerheart.TableCounters },
            presets => Assert.All(presets, p => Assert.True(
                p.Glyph is null || GameSystemGlyphs.Known.Contains(p.Glyph),
                $"unknown glyph '{p.Glyph}' on preset '{p.Key}'")));
    }
}
