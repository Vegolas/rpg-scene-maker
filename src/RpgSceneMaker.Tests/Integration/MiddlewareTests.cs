using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

[Collection("integration")]
public class MiddlewareTests
{
    [Fact]
    public async Task Invalid_scene_payload_maps_to_400_problem()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Missing name -> ArgumentException -> 400 with a Problem body.
        var response = await client.PutAsJsonAsync("/scenes/bad", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid request", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Command_on_unconfigured_provider_maps_to_503()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Provider is tuya but unconfigured -> InvalidOperationException -> 503.
        var response = await client.GetAsync("/lights/on");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Not configured", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Activating_an_unknown_scene_returns_404()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/scenes/does-not-exist/activate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
