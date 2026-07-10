using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Services;
using Xunit;

namespace RpgSceneMaker.Tests.Store;

public class SettingsStoreTests
{
    private static LightingConfigDto Dto(string provider) =>
        new(provider, new HueConfigDto("10.0.0.1", "key", ["1"]),
            new TuyaConfigDto("10.0.0.2", "dev", "lk", "3.3", "v2"));

    [Fact]
    public void Save_updates_the_in_memory_cache_immediately()
    {
        using var db = new SqliteTestDb();
        var store = new SettingsStore(db);

        store.Save(Dto("hue"));
        Assert.Equal("hue", store.Current.Provider);
        Assert.Equal("10.0.0.1", store.Current.Hue.BridgeIp);

        store.Save(Dto("tuya"));
        Assert.Equal("tuya", store.Current.Provider);
    }

    [Fact]
    public void Saved_settings_are_persisted_for_a_fresh_store()
    {
        using var db = new SqliteTestDb();
        new SettingsStore(db).Save(Dto("hue"));

        // A second store instance loads the same row from SQLite.
        Assert.Equal("hue", new SettingsStore(db).Current.Provider);
    }
}

public class SpotifyStoreTests
{
    [Fact]
    public void Changing_client_id_wipes_refresh_token_and_device()
    {
        using var db = new SqliteTestDb();
        var store = new SpotifyStore(db);
        store.SaveConfig("app-A");
        store.SaveTokens("refresh-A");
        store.SaveDevice("dev-1", "Living Room");

        Assert.True(store.Current.IsConnected);

        store.SaveConfig("app-B");   // different app -> old tokens are invalid

        Assert.Equal("app-B", store.Current.ClientId);
        Assert.Equal("", store.Current.RefreshToken);
        Assert.Equal("", store.Current.PreferredDeviceId);
        Assert.False(store.Current.IsConnected);
    }

    [Fact]
    public void Resaving_the_same_client_id_preserves_tokens_and_device()
    {
        using var db = new SqliteTestDb();
        var store = new SpotifyStore(db);
        store.SaveConfig("app-A");
        store.SaveTokens("refresh-A");
        store.SaveDevice("dev-1", "Living Room");

        store.SaveConfig("app-A");   // no change

        Assert.Equal("refresh-A", store.Current.RefreshToken);
        Assert.Equal("dev-1", store.Current.PreferredDeviceId);
        Assert.True(store.Current.IsConnected);
    }
}
