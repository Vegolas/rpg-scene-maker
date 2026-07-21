using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// The board "fear" element (issue #144): a geometry-only live placeholder — the twin of party/enemies — that
/// renders the table-level counter whose semantic Key == "fear" as a skull track. Covers the /tv/state
/// inlining (the element carries the roster's table counters, each with its semantic key) and the rev-bump
/// idiom (a Fear change refreshes a shown fear board, and nothing else).
/// </summary>
[Collection("integration")]
public class FearTests
{
    private static long Rev(JsonElement state) => state.GetProperty("rev").GetInt64();

    // ---- 1. A fear board inlines the fear-keyed table counter on /tv/state ----

    [Fact]
    public async Task Fear_only_board_inlines_the_fear_keyed_table_counter()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A table-level Fear counter carrying the semantic key "fear" — the fear element matches on the key,
        // never the localized label.
        (await client.PutAsJsonAsync("/party/counters", new object[]
        {
            new { label = "Fear", value = 3, max = 12, style = "pips", key = "fear" },
        })).EnsureSuccessStatusCode();

        // A board with ONLY a fear element (no party/enemies element).
        (await client.PutAsJsonAsync("/boards/dread", new
        {
            name = "Dread",
            elements = new object[] { new { kind = "fear", x = 25.0, y = 4.0, w = 50.0, h = 13.0 } },
        })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=dread")).StatusCode);

        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        var els = state.GetProperty("content").GetProperty("board").GetProperty("elements");
        Assert.Equal(1, els.GetArrayLength());

        var el = els[0];
        Assert.Equal("fear", el.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, el.GetProperty("url").ValueKind); // a live placeholder streams no url

        // The fear-keyed table counter rides along on the element's party payload, WITH its semantic key so the
        // client can find it despite a localized label.
        var counters = el.GetProperty("party").GetProperty("counters");
        Assert.Equal(1, counters.GetArrayLength());
        Assert.Equal("Fear", counters[0].GetProperty("label").GetString());
        Assert.Equal("fear", counters[0].GetProperty("key").GetString());
        Assert.Equal(3, counters[0].GetProperty("value").GetInt32());
        Assert.Equal(12, counters[0].GetProperty("max").GetInt32());
    }

    // ---- 2. The rev-bump idiom extends to fear-only boards (mirrors the enemies-board test) ----

    [Fact]
    public async Task Fear_changes_bump_rev_while_a_fear_board_is_shown_and_not_otherwise()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/party/counters", new object[]
        {
            new { label = "Fear", value = 3, max = 12, style = "pips", key = "fear" },
        })).EnsureSuccessStatusCode();
        // A board with ONLY a fear element.
        (await client.PutAsJsonAsync("/boards/fear-only", new
        {
            name = "Fear",
            elements = new object[] { new { kind = "fear", x = 25.0, y = 4.0, w = 50.0, h = 13.0 } },
        })).EnsureSuccessStatusCode();
        // A plain board with no live-table element.
        (await client.PutAsJsonAsync("/boards/plain", new { name = "Plain" })).EnsureSuccessStatusCode();

        // While a fear board is shown, a Fear adjust bumps the rev (matched by the semantic key).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=fear-only")).StatusCode);
        var rev1 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        (await client.GetAsync("/party/counters/adjust?counter=fear&delta=1")).EnsureSuccessStatusCode();
        var rev2 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.True(rev2 > rev1, "a Fear adjust while a fear board is shown should bump the rev");

        // Now show a board with NO live-table element — a Fear adjust must NOT bump the rev.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=plain")).StatusCode);
        var revPlain = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        (await client.GetAsync("/party/counters/adjust?counter=fear&delta=1")).EnsureSuccessStatusCode();
        var revPlainAfter = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.Equal(revPlain, revPlainAfter);
    }
}
