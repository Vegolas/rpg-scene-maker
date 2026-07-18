using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

/// <summary>
/// The player-facing "/tv" display: show/clear round-trip, the current-content stream, the recent list,
/// and — most importantly — the access-control split (state/content stay outside the API-key gate so a
/// shared table screen never carries the admin key; the GM push commands and history are gated).
/// </summary>
[Collection("integration")]
public class TvTests
{
    private const string Key = "s3cret";

    // A 1x1 transparent PNG — the smallest valid upload for the /images → /tv flow.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static async Task<string> UploadPngAsync(HttpClient client, string? apiKey = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Convert.FromBase64String(TinyPngBase64));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "handout.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/images/upload") { Content = form };
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task State_starts_at_rev_1_with_nothing_shown()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");

        Assert.Equal(1, state.GetProperty("rev").GetInt64());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("content").ValueKind);

        // Nothing shown -> nothing to stream.
        var content = await client.GetAsync("/tv/content/current");
        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);
    }

    [Fact]
    public async Task Show_state_content_clear_roundtrip()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var stored = await UploadPngAsync(client);

        // Push it (GET works — Stream Deck convention) with a label.
        var show = await client.GetAsync($"/tv/show?image={stored}&label=Tavern%20map");
        Assert.Equal(HttpStatusCode.OK, show.StatusCode);

        // State: rev bumped past the initial 1, content present and pointing at the streaming route.
        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state?rev=1");
        var rev = state.GetProperty("rev").GetInt64();
        Assert.True(rev > 1);
        var content = state.GetProperty("content");
        Assert.Equal("image", content.GetProperty("kind").GetString());
        Assert.Equal($"/tv/content/current?rev={rev}", content.GetProperty("url").GetString());
        Assert.Equal("Tavern map", content.GetProperty("label").GetString());

        // The current image streams with its real content type and bytes.
        var bytes = await client.GetAsync("/tv/content/current");
        Assert.Equal(HttpStatusCode.OK, bytes.StatusCode);
        Assert.Equal("image/png", bytes.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Convert.FromBase64String(TinyPngBase64), await bytes.Content.ReadAsByteArrayAsync());

        // Clear: content null again, rev bumped again, stream 404s.
        var clear = await client.GetAsync("/tv/clear");
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        Assert.True(cleared.GetProperty("rev").GetInt64() > rev);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("content").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tv/content/current")).StatusCode);
    }

    [Fact]
    public async Task Show_with_unknown_image_is_400_with_stable_code()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/tv/show?image=no-such-image.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.tv.imageNotFound", problem.GetProperty("code").GetString());
        Assert.Equal("no-such-image.png", problem.GetProperty("args")[0].GetString());
    }

    [Fact]
    public async Task Show_with_traversal_name_is_400_not_a_file_probe()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // FullPathForName rejects anything that isn't a plain "slug.ext" stored name.
        var response = await client.GetAsync("/tv/show?image=..%2Fappsettings.json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.tv.imageNotFound", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Recent_is_newest_first_and_deduped_by_file()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var first = await UploadPngAsync(client);
        var second = await UploadPngAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?image={first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?image={second}")).StatusCode);
        // Re-push the first: it moves to the front instead of duplicating.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?image={first}")).StatusCode);

        var recent = await client.GetFromJsonAsync<JsonElement>("/tv/show/recent");
        Assert.Equal(2, recent.GetArrayLength());
        Assert.Equal(first, recent[0].GetProperty("file").GetString());
        Assert.Equal(second, recent[1].GetProperty("file").GetString());
    }

    // ---- The access-control matrix (the flagged decision on issue #80) ----

    [Fact]
    public async Task State_and_current_content_are_exempt_from_the_key_gate()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        // The TV never holds the key: state polls fine without one.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/state")).StatusCode);

        // Push something (with the key), then the content stream is also open — the pushed image being
        // visible key-free IS the feature (it's on the shared screen).
        var stored = await UploadPngAsync(client, apiKey: Key);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/tv/show?image={stored}&apiKey={Key}")).StatusCode);
        var content = await client.GetAsync("/tv/content/current");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);

        // ...but the general /images route stays locked (only the deliberately-pushed image is exposed).
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/images/{stored}")).StatusCode);
    }

    [Fact]
    public async Task Push_commands_and_history_require_the_key()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/tv/show?image=x.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/tv/clear")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/tv/show/recent")).StatusCode);

        // With the key (query form, like a Stream Deck button) they work.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/clear?apiKey={Key}")).StatusCode);
        var recent = new HttpRequestMessage(HttpMethod.Get, "/tv/show/recent");
        recent.Headers.Add("X-Api-Key", Key);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(recent)).StatusCode);
    }
}
