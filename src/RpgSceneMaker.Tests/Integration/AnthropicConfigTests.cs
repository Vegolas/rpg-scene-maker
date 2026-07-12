using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

[Collection("integration")]
public class AnthropicConfigTests
{
    [Fact]
    public async Task Saved_api_key_is_never_echoed_by_the_config_endpoint()
    {
        const string sentinel = "sk-ant-super-secret-sentinel-value";
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/setup/anthropic/config",
            new { apiKey = sentinel, model = "claude-opus-4-8" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // Neither the PUT response nor a subsequent GET may leak the key back to the client.
        Assert.DoesNotContain(sentinel, await put.Content.ReadAsStringAsync());

        var get = await client.GetAsync("/setup/anthropic/config");
        var body = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(sentinel, body);
        Assert.Contains("\"configured\":true", body);
    }
}
