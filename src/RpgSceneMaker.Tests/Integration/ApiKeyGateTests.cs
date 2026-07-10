using System.Net;
using System.Net.Http;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

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
    public async Task Spotify_callback_is_exempt_from_the_key_gate()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        // Reachable without a key: it fails on the missing state (400), not the gate (401).
        var response = await client.GetAsync("/setup/spotify/callback");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
