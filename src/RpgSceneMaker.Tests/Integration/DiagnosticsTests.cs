using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

[Collection("integration")]
public class DiagnosticsTests
{
    [Fact]
    public async Task Diagnostics_reports_runtime_facts()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/diagnostics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var diag = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Hermetic boot: default tuya provider, nothing connected/seeded.
        Assert.Equal("tuya", diag.GetProperty("lightProvider").GetString());
        Assert.False(diag.GetProperty("spotifyConnected").GetBoolean());
        Assert.Equal(0, diag.GetProperty("sceneCount").GetInt32());
        Assert.Equal(0, diag.GetProperty("soundCount").GetInt32());
        Assert.Equal(0, diag.GetProperty("eventCount").GetInt32());

        // Present regardless of host OS (soundboardSupported is Windows-only, so only its presence
        // is asserted, not its value — the test suite also runs on Linux CI).
        Assert.False(string.IsNullOrWhiteSpace(diag.GetProperty("version").GetString()));
        Assert.True(diag.TryGetProperty("soundboardSupported", out _));
        Assert.True(diag.TryGetProperty("startedAt", out _));
    }
}
