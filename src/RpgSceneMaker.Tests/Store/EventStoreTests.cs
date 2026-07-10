using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using Xunit;

namespace RpgSceneMaker.Tests.Store;

public class EventStoreTests
{
    [Fact]
    public async Task Upsert_inserts_then_updates_including_the_flash_json_column()
    {
        using var db = new SqliteTestDb();
        var store = new EventStore(db);

        await store.UpsertAsync(new GameEvent
        {
            Id = "thunder",
            Name = "⚡ Thunder",
            Flash = new EventFlash { Color = "#FFFFFF", Brightness = 100, DurationMs = 200 },
            SoundEffects = ["thunderclap"],
        });
        await store.UpsertAsync(new GameEvent
        {
            Id = "thunder",
            Name = "Big Thunder",
            Flash = new EventFlash { Color = "#3060FF", Brightness = 50, DurationMs = 400 },
            SoundEffects = ["clap", "rumble"],
        });

        var loaded = await store.GetAsync("thunder");
        Assert.Equal("Big Thunder", loaded!.Name);
        Assert.Equal("#3060FF", loaded.Flash!.Color);
        Assert.Equal(50, loaded.Flash.Brightness);
        Assert.Equal(400, loaded.Flash.DurationMs);
        Assert.Equal(["clap", "rumble"], loaded.SoundEffects);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task Flash_is_optional_and_persists_as_null()
    {
        using var db = new SqliteTestDb();
        var store = new EventStore(db);

        await store.UpsertAsync(new GameEvent { Id = "horn", Name = "Horn Blast", SoundEffects = ["horn"] });

        var loaded = await store.GetAsync("horn");
        Assert.Null(loaded!.Flash);
        Assert.Equal(["horn"], loaded.SoundEffects);
    }

    [Fact]
    public async Task Get_matches_id_case_insensitively_and_delete_works()
    {
        using var db = new SqliteTestDb();
        var store = new EventStore(db);
        await store.UpsertAsync(new GameEvent { Id = "Thunder", Name = "Thunder" });

        Assert.NotNull(await store.GetAsync("thunder"));
        Assert.True(await store.DeleteAsync("THUNDER"));
        Assert.Null(await store.GetAsync("thunder"));
    }

    [Fact]
    public async Task GetAll_orders_by_id()
    {
        using var db = new SqliteTestDb();
        var store = new EventStore(db);
        await store.UpsertAsync(new GameEvent { Id = "zephyr", Name = "Zephyr" });
        await store.UpsertAsync(new GameEvent { Id = "anvil", Name = "Anvil" });
        await store.UpsertAsync(new GameEvent { Id = "blade", Name = "Blade" });

        var all = await store.GetAllAsync();
        Assert.Equal(["anvil", "blade", "zephyr"], all.Select(e => e.Id));
    }
}
