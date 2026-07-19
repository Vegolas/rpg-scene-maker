using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

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

    [Fact]
    public async Task Not_found_problem_carries_a_stable_machine_error_code()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Previously an ad-hoc { error = "..." } body: no code, English only. Now a coded Problem response.
        var response = await client.GetAsync("/scenes/does-not-exist/activate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Stream Deck / MCP / tests can branch on this code regardless of language.
        Assert.Equal("error.scene.notFound", problem.GetProperty("code").GetString());
        Assert.Equal("Not found", problem.GetProperty("title").GetString());
        Assert.Equal("No scene with id 'does-not-exist'. See GET /scenes.", problem.GetProperty("detail").GetString());
        // The interpolation arg (the missing id) is on the wire for machine consumers.
        Assert.Equal("does-not-exist", problem.GetProperty("args")[0].GetString());
    }

    [Fact]
    public async Task Not_found_problem_localizes_with_X_Ui_Lang()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ui-Lang", "pl");

        var response = await client.GetAsync("/events/ghost/trigger");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Same stable code, Polish human-facing strings (proves the new keys exist in pl.json).
        Assert.Equal("error.event.notFound", problem.GetProperty("code").GetString());
        Assert.Equal("Nie znaleziono", problem.GetProperty("title").GetString());
        Assert.Equal("Brak zdarzenia o identyfikatorze 'ghost'. Zobacz GET /events/list.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Validation_problem_carries_a_stable_machine_error_code()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/scenes/bad", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        // The client / Stream Deck / MCP can branch on this code regardless of the message language.
        Assert.Equal("error.common.nameRequired", problem.GetProperty("code").GetString());
        // No header -> English title/detail (unchanged from before this feature).
        Assert.Equal("Invalid request", problem.GetProperty("title").GetString());
        Assert.Equal("Name is required.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task X_Ui_Lang_localizes_the_title_and_detail()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ui-Lang", "pl");

        var response = await client.PutAsJsonAsync("/scenes/bad", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Same stable code, but the human-facing strings are now Polish.
        Assert.Equal("error.common.nameRequired", problem.GetProperty("code").GetString());
        Assert.Equal("Nieprawidłowe żądanie", problem.GetProperty("title").GetString());
        Assert.Equal("Nazwa jest wymagana.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Unknown_X_Ui_Lang_falls_back_to_english()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ui-Lang", "xx");

        var response = await client.PutAsJsonAsync("/scenes/bad", new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Name is required.", problem.GetProperty("detail").GetString());
    }
}
