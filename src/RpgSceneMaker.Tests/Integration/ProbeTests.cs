using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

[Collection("integration")]
public class ProbeTests
{
    [Fact]
    public async Task Hermetic_boot_has_no_seeded_scenes_and_default_tuya_provider()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var scenes = await client.GetFromJsonAsync<JsonElement>("/scenes");
        Assert.Equal(0, scenes.GetArrayLength());

        var config = await client.GetFromJsonAsync<JsonElement>("/setup/config");
        Assert.Equal("tuya", config.GetProperty("provider").GetString());
    }
}
