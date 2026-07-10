using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

[Collection("integration")]
public class ActivationTests
{
    [Fact]
    public async Task Scene_touching_nothing_activates_200_all_skipped()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/scenes/empty", new { name = "Empty" })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/scenes/empty/activate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("fullySucceeded").GetBoolean());
        Assert.Equal("skipped", body.GetProperty("music").GetString());
        Assert.Equal("skipped", body.GetProperty("light").GetString());
    }

    [Fact]
    public async Task Music_that_fails_yields_207_with_per_part_status()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Valid Spotify URI, but Spotify is not connected -> the music part errors at activation time.
        (await client.PutAsJsonAsync("/scenes/song", new
        {
            name = "Song",
            music = new { playId = "spotify:track:4uLU6hMCjMI75M1A2tKUQC" },
        })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/scenes/song/activate");

        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);   // 207
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("fullySucceeded").GetBoolean());
        Assert.StartsWith("error", body.GetProperty("music").GetString());
        Assert.Equal("skipped", body.GetProperty("light").GetString());
    }
}

[Collection("integration")]
public class GetOrPostContractTests
{
    [Fact]
    public async Task Activate_command_reachable_by_both_get_and_post()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/scenes/empty", new { name = "Empty" })).EnsureSuccessStatusCode();

        var get = await client.GetAsync("/scenes/empty/activate");
        var post = await client.PostAsync("/scenes/empty/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }
}
