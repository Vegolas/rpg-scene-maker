using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Services.Images;
using Xunit;

namespace AmbientDirector.Tests.Integration;

[Collection("integration")]
public class ImageSearchTests
{
    [Fact]
    public async Task Sources_lists_scryfall()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var sources = await client.GetFromJsonAsync<JsonElement>("/images/sources");

        Assert.Equal(JsonValueKind.Array, sources.ValueKind);
        var scryfall = sources.EnumerateArray().Single(s => s.GetProperty("id").GetString() == "scryfall");
        Assert.Equal("Scryfall", scryfall.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(scryfall.GetProperty("attribution").GetString()));
    }

    [Fact]
    public async Task Search_with_blank_q_is_400_queryRequired()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/images/search?source=scryfall&q=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.imageSearch.queryRequired", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Search_with_unknown_source_is_400_unknownSource()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/images/search?source=bogus&q=tavern");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.imageSearch.unknownSource", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Import_with_missing_url_is_400_urlRequired()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/images/import", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.imageImport.urlRequired", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Import_from_a_non_allowlisted_host_is_400_hostNotAllowed()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A well-formed https URL, but no registered source vouches for the host → rejected before any fetch.
        var response = await client.PostAsJsonAsync("/images/import", new { url = "https://evil.example/x.jpg" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.imageImport.hostNotAllowed", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Import_happy_path_with_a_fake_source_stores_and_serves_the_image()
    {
        using var factory = new ApiFactory();
        var client = factory
            .WithWebHostBuilder(b => b.ConfigureTestServices(s =>
                s.AddSingleton<IImageSearchSource, FakeImageSource>()))
            .CreateClient();

        var response = await client.PostAsJsonAsync("/images/import", new { url = "https://img.test/pic.png" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.EndsWith(".png", id);

        // The stored file is now served back by GET /images/{name}.
        var fetched = await client.GetAsync($"/images/{id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
    }

    // A minimal in-process image source so the import happy path runs with no network. It vouches for its
    // own https host and returns a tiny PNG with the right Content-Type.
    private sealed class FakeImageSource : IImageSearchSource
    {
        public string Id => "fake";
        public string Name => "Fake";
        public string Attribution => "Test source.";

        public Task<ImageSearchResponseDto> SearchAsync(string query, CancellationToken ct) =>
            Task.FromResult(new ImageSearchResponseDto(Id, 0, false, []));

        public bool CanFetch(Uri url) =>
            url.Scheme == Uri.UriSchemeHttps && url.Host == "img.test";

        public Task<HttpResponseMessage> FetchImageAsync(Uri url, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, url),   // for the redirect guard
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        }
    }
}
