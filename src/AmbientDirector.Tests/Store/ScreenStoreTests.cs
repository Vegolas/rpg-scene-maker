using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using Xunit;

namespace AmbientDirector.Tests.Store;

public class ScreenStoreTests
{
    [Fact]
    public async Task Upsert_inserts_then_updates_including_the_tiles_json_column()
    {
        using var db = new SqliteTestDb();
        var store = new ScreenStore(db);

        await store.UpsertAsync(new Screen
        {
            Id = "fantasy",
            Name = "🗺️ Fantasy",
            Tiles = [new ScreenTile { Kind = "scene", Ref = "tavern" }],
        });
        await store.UpsertAsync(new Screen
        {
            Id = "fantasy",
            Name = "Big Fantasy",
            Tiles =
            [
                new ScreenTile { Kind = "scene", Ref = "dungeon" },
                new ScreenTile { Kind = "music", Ref = "spotify:playlist:37i9dQ", Label = "Epic" },
            ],
        });

        var loaded = await store.GetAsync("fantasy");
        Assert.Equal("Big Fantasy", loaded!.Name);
        Assert.Equal(2, loaded.Tiles.Count);
        Assert.Equal("scene", loaded.Tiles[0].Kind);
        Assert.Equal("dungeon", loaded.Tiles[0].Ref);
        Assert.Equal("music", loaded.Tiles[1].Kind);
        Assert.Equal("Epic", loaded.Tiles[1].Label);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task Tiles_default_to_an_empty_list()
    {
        using var db = new SqliteTestDb();
        var store = new ScreenStore(db);

        await store.UpsertAsync(new Screen { Id = "empty", Name = "Empty" });

        var loaded = await store.GetAsync("empty");
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Tiles);
    }

    [Fact]
    public async Task Get_matches_id_case_insensitively_and_delete_works()
    {
        using var db = new SqliteTestDb();
        var store = new ScreenStore(db);
        await store.UpsertAsync(new Screen { Id = "Fantasy", Name = "Fantasy" });

        Assert.NotNull(await store.GetAsync("fantasy"));
        Assert.True(await store.DeleteAsync("FANTASY"));
        Assert.Null(await store.GetAsync("fantasy"));
    }

    [Fact]
    public async Task GetAll_orders_by_id()
    {
        using var db = new SqliteTestDb();
        var store = new ScreenStore(db);
        await store.UpsertAsync(new Screen { Id = "zephyr", Name = "Zephyr" });
        await store.UpsertAsync(new Screen { Id = "anvil", Name = "Anvil" });
        await store.UpsertAsync(new Screen { Id = "blade", Name = "Blade" });

        var all = await store.GetAllAsync();
        Assert.Equal(["anvil", "blade", "zephyr"], all.Select(s => s.Id));
    }
}
