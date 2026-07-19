using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

[Collection("integration")]
public class AssistantConfigTests
{
    [Fact]
    public async Task Saved_api_key_is_never_echoed_by_the_config_endpoint()
    {
        const string sentinel = "sk-super-secret-sentinel-value";
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var put = await client.PutAsJsonAsync("/setup/assistant/config",
            new { provider = "openai", apiKey = sentinel, model = "gpt-4o" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        // Neither the PUT response nor a subsequent GET may leak the key back to the client.
        Assert.DoesNotContain(sentinel, await put.Content.ReadAsStringAsync());

        var get = await client.GetAsync("/setup/assistant/config");
        var body = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(sentinel, body);
        Assert.Contains("\"configured\":true", body);
    }

    [Fact]
    public async Task Provider_and_model_round_trip_through_the_config_endpoint()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        await client.PutAsJsonAsync("/setup/assistant/config",
            new { provider = "gemini", apiKey = "AIza-test-key", model = "gemini-2.0-flash" });

        var body = await (await client.GetAsync("/setup/assistant/config")).Content.ReadAsStringAsync();
        Assert.Contains("\"provider\":\"gemini\"", body);
        Assert.Contains("\"model\":\"gemini-2.0-flash\"", body);

        // Disconnect forgets the key but keeps provider + model.
        var disc = await (await client.GetAsync("/setup/assistant/disconnect")).Content.ReadAsStringAsync();
        Assert.Contains("\"configured\":false", disc);
        Assert.Contains("\"provider\":\"gemini\"", disc);
    }
}
