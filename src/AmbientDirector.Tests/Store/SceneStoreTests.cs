using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using Xunit;

namespace AmbientDirector.Tests.Store;

public class SceneStoreTests
{
    [Fact]
    public async Task Upsert_inserts_a_new_scene_with_its_json_owned_collections()
    {
        using var db = new SqliteTestDb();
        var store = new SceneStore(db);

        await store.UpsertAsync(new Scene
        {
            Id = "tavern",
            Name = "🍺 Tavern",
            Lights =
            [
                new SceneLight
                {
                    LightKey = "lamp",
                    Color = "#FF8C2A",
                    Brightness = 70,
                    Effect = new LightEffect { Type = "drift", Speed = 4, Intensity = 6, Colors = ["#FF0000", "#00FF00"] },
                },
            ],
            SoundEffects = ["crackle"],
        });

        var loaded = await store.GetAsync("tavern");
        Assert.NotNull(loaded);
        Assert.Equal("🍺 Tavern", loaded!.Name);
        var light = Assert.Single(loaded.Lights);
        Assert.Equal("lamp", light.LightKey);
        Assert.Equal("#FF8C2A", light.Color);
        Assert.Equal("drift", light.Effect!.Type);
        Assert.Equal(["#FF0000", "#00FF00"], light.Effect.Colors);
        Assert.Equal(["crackle"], loaded.SoundEffects);
    }

    [Fact]
    public async Task Upsert_updates_existing_scene_replacing_owned_collections()
    {
        using var db = new SqliteTestDb();
        var store = new SceneStore(db);

        await store.UpsertAsync(new Scene
        {
            Id = "tavern",
            Name = "Old",
            Lights = [new SceneLight { LightKey = "a" }],
            SoundEffects = ["old-sound"],
        });

        await store.UpsertAsync(new Scene
        {
            Id = "tavern",
            Name = "New",
            Lights = [new SceneLight { LightKey = "b" }, new SceneLight { LightKey = "c" }],
            SoundEffects = ["new-sound"],
        });

        var loaded = await store.GetAsync("tavern");
        Assert.Equal("New", loaded!.Name);
        Assert.Equal(["b", "c"], loaded.Lights.Select(l => l.LightKey));
        Assert.Equal(["new-sound"], loaded.SoundEffects);

        Assert.Single(await store.GetAllAsync());   // still one row, not two
    }

    [Fact]
    public async Task Get_matches_id_case_insensitively()
    {
        using var db = new SqliteTestDb();
        var store = new SceneStore(db);
        await store.UpsertAsync(new Scene { Id = "Tavern", Name = "Tavern" });

        Assert.NotNull(await store.GetAsync("tavern"));
        Assert.NotNull(await store.GetAsync("TAVERN"));
    }

    [Fact]
    public async Task Delete_removes_the_scene()
    {
        using var db = new SqliteTestDb();
        var store = new SceneStore(db);
        await store.UpsertAsync(new Scene { Id = "tavern", Name = "Tavern" });

        Assert.True(await store.DeleteAsync("tavern"));
        Assert.Null(await store.GetAsync("tavern"));
        Assert.False(await store.DeleteAsync("tavern"));   // already gone
    }
}
