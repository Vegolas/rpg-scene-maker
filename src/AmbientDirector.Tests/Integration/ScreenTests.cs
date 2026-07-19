using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

[Collection("integration")]
public class ScreenTests
{
    [Fact]
    public async Task Put_then_list_round_trips_the_tiles()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/screens/fantasy", new
        {
            name = "🗺️ Fantasy",
            tiles = new object[]
            {
                new { kind = "scene", @ref = "tavern", label = "" },
                new { kind = "music", @ref = "spotify:playlist:37i9dQZF1DX8NTLI2TtZa6", label = "Epic Score" },
                new { kind = "light-reset", @ref = "", label = "" },
            },
        })).EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<JsonElement>("/screens/list");
        var screen = list.EnumerateArray().Single(s => s.GetProperty("id").GetString() == "fantasy");
        Assert.Equal("🗺️ Fantasy", screen.GetProperty("name").GetString());

        var tiles = screen.GetProperty("tiles");
        Assert.Equal(3, tiles.GetArrayLength());
        Assert.Equal("scene", tiles[0].GetProperty("kind").GetString());
        Assert.Equal("tavern", tiles[0].GetProperty("ref").GetString());
        Assert.Equal("music", tiles[1].GetProperty("kind").GetString());
        Assert.Equal("Epic Score", tiles[1].GetProperty("label").GetString());
        // light-reset normalises away any stray ref.
        Assert.Equal("", tiles[2].GetProperty("ref").GetString());
    }

    [Fact]
    public async Task Get_one_screen_is_not_an_api_route_so_full_page_loads_reach_the_spa()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/screens/fantasy", new { name = "Fantasy" })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/screens/fantasy");

        // There is deliberately no GET /screens/{id} API route (the panel reads /screens/list and picks by
        // id), so a full-page GET must NOT return a JSON screen — it falls through to the SPA host instead.
        Assert.NotEqual("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Empty_screen_round_trips()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/screens/blank", new { name = "Blank" })).EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<JsonElement>("/screens/list");
        var screen = list.EnumerateArray().Single(s => s.GetProperty("id").GetString() == "blank");
        Assert.Empty(screen.GetProperty("tiles").EnumerateArray());
    }

    [Fact]
    public async Task Missing_name_is_rejected_400()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/screens/noname", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("bogus", "x", "L")]              // unknown kind
    [InlineData("music", "not-a-spotify-uri", "L")] // music ref must be a Spotify URI/link
    [InlineData("music", "spotify:playlist:x", "")] // music tile needs a label
    [InlineData("scene", "", "L")]                // scene tile needs a ref
    public async Task Invalid_tiles_are_rejected_400(string kind, string reference, string label)
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/screens/bad", new
        {
            name = "Bad",
            tiles = new object[] { new { kind, @ref = reference, label } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_screen()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/screens/gone", new { name = "Gone" })).EnsureSuccessStatusCode();

        var delete = await client.DeleteAsync("/screens/gone");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await client.GetFromJsonAsync<JsonElement>("/screens/list");
        Assert.DoesNotContain(list.EnumerateArray(), s => s.GetProperty("id").GetString() == "gone");
    }

    [Fact]
    public async Task Deleting_a_missing_screen_is_404()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/screens/ghost");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
