using System.Net;
using System.Net.Http;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Tests.Store;
using Xunit;

namespace RpgSceneMaker.Tests.Http;

public class SpotifyClientTests
{
    private const string TokenOk = "{\"access_token\":\"tok1\",\"expires_in\":3600}";

    private static SpotifyStore Connected(SqliteTestDb db, string? deviceId = null)
    {
        var store = new SpotifyStore(db);
        store.SaveConfig("client-id");
        store.SaveTokens("refresh-token");
        if (deviceId is not null) store.SaveDevice(deviceId, "Living Room");
        return store;
    }

    private static (SpotifyClient client, FakeHttpMessageHandler handler) Build(SpotifyStore store)
    {
        var handler = new FakeHttpMessageHandler();
        return (new SpotifyClient(new HttpClient(handler), store, new SpotifyTokenCache()), handler);
    }

    [Fact]
    public async Task Not_connected_throws_InvalidOperationException_without_calling_the_api()
    {
        using var db = new SqliteTestDb();
        var (client, handler) = Build(new SpotifyStore(db));   // never connected

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PlayAsync("spotify:track:abc"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Access_token_is_refreshed_once_then_cached()
    {
        using var db = new SqliteTestDb();
        var (client, handler) = Build(Connected(db));
        handler.Enqueue(HttpStatusCode.OK, TokenOk);

        var first = await client.GetAccessTokenAsync();
        var second = await client.GetAccessTokenAsync();

        Assert.Equal("tok1", first);
        Assert.Equal("tok1", second);
        // Only one token exchange despite two calls (the second is served from cache).
        Assert.Single(handler.Requests);
        var req = handler.Requests[0];
        Assert.Equal("https://accounts.spotify.com/api/token", req.Uri.ToString());
        Assert.Contains("grant_type=refresh_token", req.Body);
        Assert.Contains("refresh_token=refresh-token", req.Body);
        Assert.Contains("client_id=client-id", req.Body);
    }

    [Fact]
    public async Task Play_retries_without_device_when_preferred_device_is_gone()
    {
        using var db = new SqliteTestDb();
        var (client, handler) = Build(Connected(db, deviceId: "dev1"));
        handler
            .Enqueue(HttpStatusCode.OK, TokenOk)          // token exchange
            .Enqueue(HttpStatusCode.NotFound, "{}")       // play on preferred device -> gone
            .Enqueue(HttpStatusCode.NoContent);           // fallback play succeeds

        await client.PlayAsync("spotify:track:abc");

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("device_id=dev1", handler.Requests[1].Uri.Query);
        Assert.Equal("https://api.spotify.com/v1/me/player/play", handler.Requests[2].Uri.ToString());
        Assert.Contains("spotify:track:abc", handler.Requests[1].Body);   // track -> uris body
    }

    [Fact]
    public async Task Play_403_premium_required_maps_to_friendly_message()
    {
        using var db = new SqliteTestDb();
        var (client, handler) = Build(Connected(db));
        handler
            .Enqueue(HttpStatusCode.OK, TokenOk)
            .Enqueue(HttpStatusCode.Forbidden, "{\"error\":{\"status\":403,\"reason\":\"PREMIUM_REQUIRED\"}}");

        var ex = await Assert.ThrowsAsync<SpotifyException>(() => client.PlayAsync("spotify:track:abc"));
        Assert.Contains("Premium", ex.Message);
    }

    [Fact]
    public async Task Play_404_maps_to_no_active_device_message()
    {
        using var db = new SqliteTestDb();
        var (client, handler) = Build(Connected(db));
        handler
            .Enqueue(HttpStatusCode.OK, TokenOk)
            .Enqueue(HttpStatusCode.NotFound, "{}");

        var ex = await Assert.ThrowsAsync<SpotifyException>(() => client.PlayAsync("spotify:track:abc"));
        Assert.Contains("No active Spotify device", ex.Message);
    }
}
