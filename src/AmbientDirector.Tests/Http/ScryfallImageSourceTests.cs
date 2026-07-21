using System.Net;
using Microsoft.Extensions.Caching.Memory;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services.Images;
using Xunit;

namespace AmbientDirector.Tests.Http;

public class ScryfallImageSourceTests
{
    // A list with: a normal single-faced card (full set of image sizes), a double-faced card (each face has
    // its own image_uris + name; the second face has NO artist so it falls back to the card artist, and only
    // an art_crop so full-image mode must fall back to it), and an imageless card that must be skipped.
    private const string ListJson = """
    {
      "object": "list",
      "total_cards": 42,
      "has_more": false,
      "data": [
        {
          "name": "Tavern Swindler",
          "artist": "Volkan Baga",
          "image_uris": {
            "small": "https://cards.scryfall.io/small/normal.jpg",
            "normal": "https://cards.scryfall.io/normal/normal.jpg",
            "large": "https://cards.scryfall.io/large/normal.jpg",
            "png": "https://cards.scryfall.io/png/normal.png",
            "art_crop": "https://cards.scryfall.io/art_crop/normal.jpg"
          }
        },
        {
          "name": "Delver of Secrets // Insectile Aberration",
          "artist": "Nils Hamm",
          "card_faces": [
            {
              "name": "Delver of Secrets",
              "artist": "Face One",
              "image_uris": {
                "normal": "https://cards.scryfall.io/normal/face1.jpg",
                "large": "https://cards.scryfall.io/large/face1.jpg",
                "art_crop": "https://cards.scryfall.io/art_crop/face1.jpg"
              }
            },
            {
              "name": "Insectile Aberration",
              "image_uris": { "art_crop": "https://cards.scryfall.io/art_crop/face2.jpg" }
            }
          ]
        },
        {
          "name": "Spirit Token",
          "artist": "Some Artist"
        }
      ]
    }
    """;

    private const string EmptyList = """{"object":"list","total_cards":0,"has_more":false,"data":[]}""";

    private static (ScryfallImageSource src, FakeHttpMessageHandler handler, HttpClient http) Build()
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler);
        var src = new ScryfallImageSource(http, new MemoryCache(new MemoryCacheOptions()));
        return (src, handler, http);
    }

    [Fact]
    public async Task Maps_cards_and_faces_skipping_the_imageless_card()
    {
        var (src, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListJson);

        var resp = await src.SearchAsync("tavern", ImageSearchOptions.Default, CancellationToken.None);

        Assert.Equal("scryfall", resp.Source);
        Assert.Equal(42, resp.Total);
        Assert.False(resp.HasMore);
        // 1 (normal) + 2 (DFC faces) + 0 (imageless card skipped) = 3.
        Assert.Equal(3, resp.Results.Count);

        Assert.Equal("Tavern Swindler", resp.Results[0].Title);
        Assert.Equal("Volkan Baga", resp.Results[0].Detail);
        Assert.Equal("https://cards.scryfall.io/art_crop/normal.jpg", resp.Results[0].Url);
        Assert.Equal(resp.Results[0].Url, resp.Results[0].ThumbUrl);   // art_crop for both

        Assert.Equal("Delver of Secrets", resp.Results[1].Title);
        Assert.Equal("Face One", resp.Results[1].Detail);
        Assert.Equal("https://cards.scryfall.io/art_crop/face1.jpg", resp.Results[1].Url);

        Assert.Equal("Insectile Aberration", resp.Results[2].Title);
        Assert.Equal("Nils Hamm", resp.Results[2].Detail);   // face has no artist → card artist fallback
        Assert.Equal("https://cards.scryfall.io/art_crop/face2.jpg", resp.Results[2].Url);
    }

    [Fact]
    public async Task FullImage_maps_the_whole_card_scan_and_falls_back_when_a_size_is_missing()
    {
        var (src, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListJson);

        var resp = await src.SearchAsync("tavern", new ImageSearchOptions(FullImage: true), CancellationToken.None);

        Assert.Equal(3, resp.Results.Count);

        // Full card: mid-size scan in the grid, high-res JPEG to import + crop.
        Assert.Equal("https://cards.scryfall.io/normal/normal.jpg", resp.Results[0].ThumbUrl);
        Assert.Equal("https://cards.scryfall.io/large/normal.jpg", resp.Results[0].Url);

        // Face one has normal + large (but no small/png) → still resolves both cleanly.
        Assert.Equal("https://cards.scryfall.io/normal/face1.jpg", resp.Results[1].ThumbUrl);
        Assert.Equal("https://cards.scryfall.io/large/face1.jpg", resp.Results[1].Url);

        // Face two has ONLY art_crop → both thumb and url fall back to it.
        Assert.Equal("https://cards.scryfall.io/art_crop/face2.jpg", resp.Results[2].ThumbUrl);
        Assert.Equal("https://cards.scryfall.io/art_crop/face2.jpg", resp.Results[2].Url);
    }

    [Fact]
    public async Task IncludeExtras_appends_the_scryfall_filter_to_the_query()
    {
        var (src, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, EmptyList);

        await src.SearchAsync("goblin", new ImageSearchOptions(IncludeExtras: true), CancellationToken.None);

        var query = Uri.UnescapeDataString(handler.Requests[0].Uri.Query);
        Assert.Contains("q=goblin include:extras", query);
        Assert.Contains("unique=art", handler.Requests[0].Uri.Query);
    }

    [Fact]
    public async Task Different_options_do_not_share_a_cache_entry()
    {
        var (src, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListJson);   // art-crop search
        handler.Enqueue(HttpStatusCode.OK, ListJson);   // full-image search hits the network too

        var art = await src.SearchAsync("tavern", ImageSearchOptions.Default, CancellationToken.None);
        var full = await src.SearchAsync("tavern", new ImageSearchOptions(FullImage: true), CancellationToken.None);

        // Two network calls (the options differ → different cache keys), and the mapped URLs differ.
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("art_crop/normal.jpg", art.Results[0].Url);
        Assert.EndsWith("large/normal.jpg", full.Results[0].Url);
    }

    [Fact]
    public async Task Search_sends_ua_accept_and_unique_art_with_escaped_query()
    {
        var (src, handler, http) = Build();
        handler.Enqueue(HttpStatusCode.OK, EmptyList);

        await src.SearchAsync("goblin king", ImageSearchOptions.Default, CancellationToken.None);

        // The typed client carries the descriptive User-Agent + Accept Scryfall requires (set in the ctor,
        // so every request sends them).
        Assert.Equal("AmbientDirector/1.0 (+https://github.com/Vegolas/ambient-director)",
            http.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Contains(http.DefaultRequestHeaders.Accept, a => a.MediaType == "application/json");

        var uri = handler.Requests[0].Uri;
        Assert.Equal("api.scryfall.com", uri.Host);
        Assert.Equal("/cards/search", uri.AbsolutePath);
        Assert.Contains("unique=art", uri.Query);
        Assert.Contains("q=goblin%20king", uri.Query);   // query is percent-escaped
    }

    [Fact]
    public async Task NotFound_error_body_maps_to_empty_results()
    {
        var (src, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.NotFound,
            """{"object":"error","code":"not_found","status":404,"details":"No cards found."}""");

        var resp = await src.SearchAsync("asdkjhaskdjh", ImageSearchOptions.Default, CancellationToken.None);

        Assert.Empty(resp.Results);
        Assert.Equal(0, resp.Total);
        Assert.False(resp.HasMore);
        Assert.Equal("scryfall", resp.Source);
    }

    [Fact]
    public async Task BadRequest_error_body_throws_ValidationException_badQuery()
    {
        var (src, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.BadRequest,
            """{"object":"error","code":"bad_request","status":400,"details":"All of your terms were ignored."}""");

        var ex = await Assert.ThrowsAsync<ValidationException>(() => src.SearchAsync("(", ImageSearchOptions.Default, CancellationToken.None));
        Assert.Equal("error.imageSearch.badQuery", ex.Code);
        // Scryfall's own explanation is carried as the interpolation arg.
        Assert.Contains("All of your terms were ignored.", ex.Args[0]?.ToString());
    }

    [Fact]
    public async Task ServerError_throws_ImageSourceException()
    {
        var (src, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<ImageSourceException>(() => src.SearchAsync("tavern", ImageSearchOptions.Default, CancellationToken.None));
    }

    [Fact]
    public async Task Identical_search_is_served_from_cache()
    {
        var (src, handler, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, ListJson);   // exactly one canned response

        var first = await src.SearchAsync("tavern", ImageSearchOptions.Default, CancellationToken.None);
        var second = await src.SearchAsync("  Tavern  ", ImageSearchOptions.Default, CancellationToken.None);   // same normalized key

        // The second call hit the cache, not the network — the handler saw only one request (and never ran
        // out of canned responses).
        Assert.Single(handler.Requests);
        Assert.Equal(first.Total, second.Total);
        Assert.Equal(first.Results.Count, second.Results.Count);
    }
}
