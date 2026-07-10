using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using Xunit;

namespace RpgSceneMaker.Tests.Store;

public class SoundStoreTests
{
    [Fact]
    public async Task Upsert_inserts_then_updates()
    {
        using var db = new SqliteTestDb();
        var store = new SoundStore(db);

        await store.UpsertAsync(new Sound { Id = "thunder", Name = "Thunder", Category = "Weather", FileName = "thunder.wav", Volume = 0.8, Loop = false });
        await store.UpsertAsync(new Sound { Id = "thunder", Name = "Big Thunder", Category = "Storm", FileName = "thunder2.wav", Volume = 0.3, Loop = true });

        var loaded = await store.GetAsync("thunder");
        Assert.Equal("Big Thunder", loaded!.Name);
        Assert.Equal("Storm", loaded.Category);
        Assert.Equal("thunder2.wav", loaded.FileName);
        Assert.Equal(0.3, loaded.Volume);
        Assert.True(loaded.Loop);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task GetAll_orders_by_category_then_name()
    {
        using var db = new SqliteTestDb();
        var store = new SoundStore(db);
        await store.UpsertAsync(new Sound { Id = "1", Name = "Zephyr", Category = "Weather" });
        await store.UpsertAsync(new Sound { Id = "2", Name = "Anvil", Category = "Combat" });
        await store.UpsertAsync(new Sound { Id = "3", Name = "Blade", Category = "Combat" });

        var all = await store.GetAllAsync();
        Assert.Equal(["Anvil", "Blade", "Zephyr"], all.Select(s => s.Name));
    }

    [Fact]
    public async Task Get_matches_id_case_insensitively_and_delete_works()
    {
        using var db = new SqliteTestDb();
        var store = new SoundStore(db);
        await store.UpsertAsync(new Sound { Id = "Thunder", Name = "Thunder" });

        Assert.NotNull(await store.GetAsync("thunder"));
        Assert.True(await store.DeleteAsync("THUNDER"));
        Assert.Null(await store.GetAsync("thunder"));
    }
}
