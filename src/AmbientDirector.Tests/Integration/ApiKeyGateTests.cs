using System.Net;
using System.Net.Http;
using Xunit;

namespace AmbientDirector.Tests.Integration;

[Collection("integration")]
public class ApiKeyGateTests
{
    private const string Key = "s3cret";

    [Fact]
    public async Task Protected_path_without_key_is_401()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/scenes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_path_with_header_key_is_allowed()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/scenes");
        request.Headers.Add("X-Api-Key", Key);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_path_with_query_key_is_allowed()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/scenes?apiKey={Key}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Diagnostics_without_key_is_401()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/diagnostics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Assistant_state_without_key_is_401()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/assistant/state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_without_key_is_401()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        // The API-key gate runs before the MCP transport, so a keyless request is rejected outright —
        // regardless of what the streamable-HTTP transport would otherwise make of a bare GET.
        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Spotify_callback_is_exempt_from_the_key_gate()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        // Reachable without a key: it fails on the missing state (400), not the gate (401).
        var response = await client.GetAsync("/setup/spotify/callback");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // The full gate matrix: every sensitive prefix is rejected without the key — including the groups added
    // after the gate was first written (#71 local music library, #73 Freesound sound library, #75 onboarding).
    // A new endpoint group that forgets to live under a protected prefix (IsProtectedPath) fails here.
    [Theory]
    [InlineData("/scenes")]
    [InlineData("/lights/list")]
    [InlineData("/music/state")]
    [InlineData("/music/library/tracks")]              // #71 local music library
    [InlineData("/music/library/playlists")]           // #71
    [InlineData("/sounds/list")]
    [InlineData("/sounds/library/search?query=door")]  // #73 Freesound library
    [InlineData("/events/list")]
    [InlineData("/screens/list")]
    [InlineData("/boards/list")]                        // Phase 2 boards
    [InlineData("/party/list")]                         // Phase 3 party tracker
    [InlineData("/party/players/x/adjust")]             // Phase 3 — the Stream Deck adjust command is gated too
    [InlineData("/lightfx/list")]
    [InlineData("/images/sources")]
    [InlineData("/setup/config")]
    [InlineData("/setup/freesound/config")]            // #73
    [InlineData("/setup/onboarding")]                  // #75 onboarding
    [InlineData("/logs/list")]
    [InlineData("/diagnostics")]
    [InlineData("/assistant/state")]
    [InlineData("/i18n/list")]
    [InlineData("/tv/show")]                            // the GM push command is gated (the display is not)
    [InlineData("/tv/clear")]
    public async Task Protected_prefix_without_key_is_401(string path)
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The deliberately key-free surface stays reachable with the gate on: the health probe and the
    // player-facing TV display (the single GM-pushed image). These must never 401, or a shared table screen
    // would need the admin key (#80). A 404 (nothing pushed yet) is fine — just not a 401.
    [Theory]
    [InlineData("/health")]
    [InlineData("/tv/state")]
    [InlineData("/tv/content/current")]
    [InlineData("/tv/content/board/x.png")]            // per-name board image route: 404s keylessly, never 401
    public async Task Open_surface_is_reachable_without_key(string path)
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
