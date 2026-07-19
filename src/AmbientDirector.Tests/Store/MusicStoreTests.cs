using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using Xunit;

namespace AmbientDirector.Tests.Store;

public class MusicTrackStoreTests
{
    [Fact]
    public async Task Upsert_inserts_then_updates()
    {
        using var db = new SqliteTestDb();
        var store = new MusicTrackStore(db);

        await store.UpsertAsync(new MusicTrack { Id = "tavern", Name = "Tavern", FileName = "tavern.mp3", DurationMs = 1000, Artist = "Anon" });
        await store.UpsertAsync(new MusicTrack { Id = "tavern", Name = "Busy Tavern", FileName = "tavern2.mp3", DurationMs = 2000, Artist = "Bard" });

        var loaded = await store.GetAsync("tavern");
        Assert.Equal("Busy Tavern", loaded!.Name);
        Assert.Equal("tavern2.mp3", loaded.FileName);
        Assert.Equal(2000, loaded.DurationMs);
        Assert.Equal("Bard", loaded.Artist);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task GetAll_orders_by_name()
    {
        using var db = new SqliteTestDb();
        var store = new MusicTrackStore(db);
        await store.UpsertAsync(new MusicTrack { Id = "1", Name = "Zephyr" });
        await store.UpsertAsync(new MusicTrack { Id = "2", Name = "Anvil" });

        var all = await store.GetAllAsync();
        Assert.Equal(["Anvil", "Zephyr"], all.Select(t => t.Name));
    }

    [Fact]
    public async Task Get_matches_id_case_insensitively_and_delete_works()
    {
        using var db = new SqliteTestDb();
        var store = new MusicTrackStore(db);
        await store.UpsertAsync(new MusicTrack { Id = "Tavern", Name = "Tavern" });

        Assert.NotNull(await store.GetAsync("tavern"));
        Assert.True(await store.DeleteAsync("TAVERN"));
        Assert.Null(await store.GetAsync("tavern"));
    }
}

public class MusicPlaylistStoreTests
{
    [Fact]
    public async Task Upsert_round_trips_ordered_track_ids()
    {
        using var db = new SqliteTestDb();
        var store = new MusicPlaylistStore(db);

        await store.UpsertAsync(new MusicPlaylist { Id = "combat", Name = "Combat", TrackIds = ["a", "b", "c"] });
        var loaded = await store.GetAsync("combat");
        Assert.Equal(["a", "b", "c"], loaded!.TrackIds);

        await store.UpsertAsync(new MusicPlaylist { Id = "combat", Name = "Combat!", TrackIds = ["c", "a"] });
        loaded = await store.GetAsync("combat");
        Assert.Equal("Combat!", loaded!.Name);
        Assert.Equal(["c", "a"], loaded.TrackIds);
    }

    [Fact]
    public async Task Delete_matches_id_case_insensitively()
    {
        using var db = new SqliteTestDb();
        var store = new MusicPlaylistStore(db);
        await store.UpsertAsync(new MusicPlaylist { Id = "Ambience", Name = "Ambience" });

        Assert.True(await store.DeleteAsync("ambience"));
        Assert.Null(await store.GetAsync("Ambience"));
    }
}
