using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using Xunit;

namespace AmbientDirector.Tests.Store;

/// <summary>The encounter store (issue #122): CRUD + ordering + the nested JSON columns (HeroIds, enemy
/// instances with their own counters), plus the tracked per-instance counter adjust (clamping, not-found
/// codes) and the reset-to-full. Mirrors <see cref="ScreenStoreTests"/> / the party store coverage.</summary>
public class EncounterStoreTests
{
    private static Encounter Sample(string id = "goblin-ambush") => new()
    {
        Id = id,
        Name = "Goblin Ambush",
        SortOrder = 0,
        HeroIds = ["kira", "aldous"],
        BackgroundImage = "forest.png",
        ActivateSceneId = "battle",
        ActivateEventId = "war-cry",
        Enemies =
        [
            new EncounterEnemy
            {
                InstanceId = "goblin-1",
                EnemyId = "goblin",
                Name = "Goblin 1",
                Portrait = "goblin.png",
                Spotlight = true,
                Counters =
                [
                    new PartyCounter { Label = "HP", Value = 6, Max = 6, Style = "number" },
                    new PartyCounter { Label = "Stress", Value = 0, Max = 4, Style = "pips" },
                ],
            },
        ],
    };

    [Fact]
    public async Task Upsert_inserts_then_updates_including_the_nested_json_columns()
    {
        using var db = new SqliteTestDb();
        var store = new EncounterStore(db);

        await store.UpsertAsync(Sample());
        var updated = Sample();
        updated.Name = "Bigger Ambush";
        updated.HeroIds = ["kira"];
        updated.Enemies[0].Name = "Goblin Boss";
        updated.Enemies.Add(new EncounterEnemy
        {
            InstanceId = "goblin-2",
            EnemyId = "goblin",
            Name = "Goblin 2",
            Counters = [new PartyCounter { Label = "HP", Value = 6, Max = 6 }],
        });
        await store.UpsertAsync(updated);

        var loaded = await store.GetAsync("goblin-ambush");
        Assert.Equal("Bigger Ambush", loaded!.Name);
        Assert.Equal(["kira"], loaded.HeroIds);
        Assert.Equal("forest.png", loaded.BackgroundImage);
        Assert.Equal("battle", loaded.ActivateSceneId);
        Assert.Equal("war-cry", loaded.ActivateEventId);
        Assert.Equal(2, loaded.Enemies.Count);
        Assert.Equal("Goblin Boss", loaded.Enemies[0].Name);
        Assert.True(loaded.Enemies[0].Spotlight);
        Assert.Equal("goblin.png", loaded.Enemies[0].Portrait);
        Assert.Equal(2, loaded.Enemies[0].Counters.Count);
        Assert.Equal("Stress", loaded.Enemies[0].Counters[1].Label);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task Lists_are_ordered_by_sortorder_then_id_and_delete_is_case_insensitive()
    {
        using var db = new SqliteTestDb();
        var store = new EncounterStore(db);
        await store.UpsertAsync(new Encounter { Id = "zephyr", Name = "Z", SortOrder = 1 });
        await store.UpsertAsync(new Encounter { Id = "anvil", Name = "A", SortOrder = 1 });
        await store.UpsertAsync(new Encounter { Id = "first", Name = "F", SortOrder = 0 });

        var all = await store.GetAllAsync();
        // SortOrder ascending (first=0), ties broken by id (anvil before zephyr).
        Assert.Equal(["first", "anvil", "zephyr"], all.Select(e => e.Id));

        Assert.NotNull(await store.GetAsync("FIRST")); // NOCASE
        Assert.True(await store.DeleteAsync("First"));
        Assert.Null(await store.GetAsync("first"));
    }

    [Fact]
    public async Task Adjust_enemy_instance_counter_clamps_into_range()
    {
        using var db = new SqliteTestDb();
        var store = new EncounterStore(db);
        await store.UpsertAsync(Sample());

        // Delta down, clamped at 0; then value set absolutely; label matched case-insensitively.
        var afterDelta = await store.AdjustEnemyInstanceAsync("goblin-ambush", "goblin-1", "HP", -999, null);
        Assert.Equal(0, Hp(afterDelta));
        var afterValue = await store.AdjustEnemyInstanceAsync("goblin-ambush", "GOBLIN-1", "hp", null, 99);
        Assert.Equal(6, Hp(afterValue)); // clamped to Max 6

        static int Hp(Encounter e) => e.Enemies[0].Counters.Single(c => c.Label == "HP").Value;
    }

    [Fact]
    public async Task Adjust_reports_not_found_for_unknown_encounter_instance_and_counter()
    {
        using var db = new SqliteTestDb();
        var store = new EncounterStore(db);
        await store.UpsertAsync(Sample());

        var noEncounter = await Assert.ThrowsAsync<NotFoundException>(
            () => store.AdjustEnemyInstanceAsync("ghost", "goblin-1", "HP", -1, null));
        Assert.Equal("error.encounter.notFound", noEncounter.Code);

        var noInstance = await Assert.ThrowsAsync<NotFoundException>(
            () => store.AdjustEnemyInstanceAsync("goblin-ambush", "nope", "HP", -1, null));
        Assert.Equal("error.encounter.enemyInstanceNotFound", noInstance.Code);

        var noCounter = await Assert.ThrowsAsync<NotFoundException>(
            () => store.AdjustEnemyInstanceAsync("goblin-ambush", "goblin-1", "Mana", -1, null));
        Assert.Equal("error.party.counterNotFound", noCounter.Code);
    }

    [Fact]
    public async Task Reset_restores_every_bounded_instance_counter_to_its_max()
    {
        using var db = new SqliteTestDb();
        var store = new EncounterStore(db);
        var encounter = Sample();
        // An extra unbounded counter to prove reset leaves it untouched.
        encounter.Enemies[0].Counters.Add(new PartyCounter { Label = "Notes", Value = 3, Max = null });
        await store.UpsertAsync(encounter);

        // Damage the instance: HP down, Stress up.
        await store.AdjustEnemyInstanceAsync("goblin-ambush", "goblin-1", "HP", -4, null);
        await store.AdjustEnemyInstanceAsync("goblin-ambush", "goblin-1", "Stress", 3, null);

        var reset = await store.ResetEnemiesAsync("goblin-ambush");
        var counters = reset.Enemies[0].Counters;
        Assert.Equal(6, counters.Single(c => c.Label == "HP").Value);     // → Max
        Assert.Equal(4, counters.Single(c => c.Label == "Stress").Value); // → Max
        Assert.Equal(3, counters.Single(c => c.Label == "Notes").Value);  // unbounded: unchanged

        var reload = await store.GetAsync("goblin-ambush");
        Assert.Equal(6, reload!.Enemies[0].Counters.Single(c => c.Label == "HP").Value); // persisted
    }

    [Fact]
    public async Task Reset_reports_not_found_for_an_unknown_encounter()
    {
        using var db = new SqliteTestDb();
        var store = new EncounterStore(db);
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => store.ResetEnemiesAsync("ghost"));
        Assert.Equal("error.encounter.notFound", ex.Code);
    }
}
