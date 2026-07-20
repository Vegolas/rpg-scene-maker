using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// The party tracker (Phase 3 of #88): players + generic counters, the single-row table-level counters, and
/// the live party render on the key-free TV. Covers the CRUD round-trip, validation codes, value clamping,
/// tap-to-adjust (delta/value, GET + POST, clamping, error codes), owned-portrait cleanup, and — most
/// importantly — the TV projection + the dynamic key-free portrait gate and the party rev-bump idiom.
/// </summary>
[Collection("integration")]
public class PartyTests
{
    private const string Key = "s3cret";

    // A 1x1 transparent PNG — the smallest valid upload for the /images → portrait flow.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static async Task<string> UploadPngAsync(HttpClient client, string? apiKey = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Convert.FromBase64String(TinyPngBase64));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "portrait.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/images/upload") { Content = form };
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    private static async Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string url, object body, string? apiKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        return await client.SendAsync(request);
    }

    private static string? Code(JsonElement problem) => problem.GetProperty("code").GetString();
    private static long Rev(JsonElement state) => state.GetProperty("rev").GetInt64();

    private static int CounterValue(JsonElement owner, string label) =>
        owner.GetProperty("counters").EnumerateArray()
            .Single(c => c.GetProperty("label").GetString() == label).GetProperty("value").GetInt32();

    // ---- 1. CRUD round-trip + ordering ----

    [Fact]
    public async Task Put_players_and_counters_round_trip_ordered_by_sortorder_and_delete_removes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Two members inserted out of roster order; SortOrder (ties → Id) decides the order.
        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            sortOrder = 1,
            counters = new object[]
            {
                new { label = "HP", value = 10, max = 12, style = "number" },
                new { label = "Hope", value = 3, max = 6, style = "pips" },
            },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/players/aldous", new { name = "Aldous", sortOrder = 0 }))
            .EnsureSuccessStatusCode();

        // Table-level counters (Fear).
        (await client.PutAsJsonAsync("/party/counters", new object[]
        {
            new { label = "Fear", value = 2, max = 12, style = "number" },
        })).EnsureSuccessStatusCode();

        var party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        var players = party.GetProperty("players");
        Assert.Equal(2, players.GetArrayLength());
        // Ordered by SortOrder: aldous (0) before kira (1).
        Assert.Equal("aldous", players[0].GetProperty("id").GetString());
        Assert.Equal("kira", players[1].GetProperty("id").GetString());

        var kira = players[1];
        Assert.Equal("Kira", kira.GetProperty("name").GetString());
        var counters = kira.GetProperty("counters");
        Assert.Equal(2, counters.GetArrayLength());
        Assert.Equal("HP", counters[0].GetProperty("label").GetString());
        Assert.Equal(10, counters[0].GetProperty("value").GetInt32());
        Assert.Equal(12, counters[0].GetProperty("max").GetInt32());
        Assert.Equal("number", counters[0].GetProperty("style").GetString());
        Assert.Equal("Hope", counters[1].GetProperty("label").GetString());
        Assert.Equal("pips", counters[1].GetProperty("style").GetString());

        var table = party.GetProperty("counters");
        Assert.Equal(1, table.GetArrayLength());
        Assert.Equal("Fear", table[0].GetProperty("label").GetString());
        Assert.Equal(2, table[0].GetProperty("value").GetInt32());

        // Delete removes the member.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/party/players/kira")).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/party/list");
        Assert.DoesNotContain(after.GetProperty("players").EnumerateArray(),
            p => p.GetProperty("id").GetString() == "kira");

        // Deleting a missing member is a 404.
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/party/players/kira")).StatusCode);
    }

    [Fact]
    public async Task Get_one_player_is_not_an_api_route_so_full_page_loads_reach_the_spa()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/party/players/kira", new { name = "Kira" })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/party/players/kira");

        // There is deliberately no GET /party/players/{id} API route (the panel reads /party/list and picks by
        // id), so a full-page GET must NOT return JSON — it falls through to the SPA host instead.
        Assert.NotEqual("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- 2. Validation → 400 with stable codes ----

    [Fact]
    public async Task Invalid_players_and_counters_are_rejected_with_stable_codes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        async Task AssertMemberCode(string id, object body, string expectedCode)
        {
            var resp = await client.PutAsJsonAsync($"/party/players/{id}", body);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal(expectedCode, Code(await resp.Content.ReadFromJsonAsync<JsonElement>()));
        }

        // Bad slug id (the space is not a slug char).
        await AssertMemberCode("bad%20name", new { name = "X" }, "error.common.idSlug");
        // Missing name.
        await AssertMemberCode("nameless", new { name = "" }, "error.common.nameRequired");
        // Bad portrait file name (traversal).
        await AssertMemberCode("kira", new { name = "Kira", portrait = "../secret.png" }, "error.common.invalidImage");
        // Bad counter style.
        await AssertMemberCode("kira", new { name = "Kira", counters = new object[] {
            new { label = "HP", value = 1, style = "bars" } } }, "error.party.counterStyle");
        // Pips without a max.
        await AssertMemberCode("kira", new { name = "Kira", counters = new object[] {
            new { label = "Hope", value = 1, style = "pips" } } }, "error.party.pipsMax");
        // Pips with a too-large max (a TV can't render 40 dots).
        await AssertMemberCode("kira", new { name = "Kira", counters = new object[] {
            new { label = "Hope", value = 1, max = 40, style = "pips" } } }, "error.party.pipsMax");
        // Missing counter label.
        await AssertMemberCode("kira", new { name = "Kira", counters = new object[] {
            new { label = "   ", value = 1 } } }, "error.party.counterLabel");
        // Duplicate counter label (case-insensitive — it's the adjust key).
        await AssertMemberCode("kira", new { name = "Kira", counters = new object[] {
            new { label = "HP", value = 1 }, new { label = "hp", value = 2 } } }, "error.party.duplicateCounter");
        // Counter max out of range.
        await AssertMemberCode("kira", new { name = "Kira", counters = new object[] {
            new { label = "HP", value = 1, max = 1000 } } }, "error.party.counterMax");

        // Too many counters (9 > 8) — exercised through the table-counters endpoint (same validator).
        var nine = Enumerable.Range(0, 9).Select(i => (object)new { label = $"C{i}", value = 0 }).ToArray();
        var tooMany = await client.PutAsJsonAsync("/party/counters", nine);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal("error.party.tooManyCounters", Code(await tooMany.Content.ReadFromJsonAsync<JsonElement>()));
    }

    // ---- 3. Value clamping on PUT (normalization, not an error) ----

    [Fact]
    public async Task Put_clamps_counter_value_into_range()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A value above the max comes back clamped to the max.
        var high = await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            counters = new object[] { new { label = "HP", value = 999, max = 6 } },
        });
        high.EnsureSuccessStatusCode();
        Assert.Equal(6, CounterValue(await high.Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // …and it persisted clamped.
        var list = await client.GetFromJsonAsync<JsonElement>("/party/list");
        var kira = list.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "kira");
        Assert.Equal(6, CounterValue(kira, "HP"));

        // A negative value clamps up to 0.
        var low = await client.PutAsJsonAsync("/party/players/aldous", new
        {
            name = "Aldous",
            counters = new object[] { new { label = "HP", value = -5, max = 10 } },
        });
        low.EnsureSuccessStatusCode();
        Assert.Equal(0, CounterValue(await low.Content.ReadFromJsonAsync<JsonElement>(), "HP"));
    }

    // ---- 4. Tap-to-adjust: delta / value, GET + POST, clamping ----

    [Fact]
    public async Task Adjust_member_counter_by_delta_and_value_via_get_and_post_with_clamping()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();

        // GET delta decrements (Stream Deck style).
        var dec = await client.GetAsync("/party/players/kira/adjust?counter=HP&delta=-2");
        dec.EnsureSuccessStatusCode();
        Assert.Equal(3, CounterValue(await dec.Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // POST delta increments.
        var inc = await client.PostAsync("/party/players/kira/adjust?counter=HP&delta=4", null);
        inc.EnsureSuccessStatusCode();
        Assert.Equal(7, CounterValue(await inc.Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // Delta clamps at Max.
        var overMax = await client.GetAsync("/party/players/kira/adjust?counter=HP&delta=999");
        Assert.Equal(10, CounterValue(await overMax.Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // Delta clamps at 0.
        var underZero = await client.GetAsync("/party/players/kira/adjust?counter=HP&delta=-999");
        Assert.Equal(0, CounterValue(await underZero.Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // Value sets absolutely.
        var setAbs = await client.GetAsync("/party/players/kira/adjust?counter=HP&value=8");
        Assert.Equal(8, CounterValue(await setAbs.Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // The counter label is matched case-insensitively.
        var ci = await client.GetAsync("/party/players/kira/adjust?counter=hp&delta=-1");
        Assert.Equal(7, CounterValue(await ci.Content.ReadFromJsonAsync<JsonElement>(), "HP"));
    }

    [Fact]
    public async Task Adjust_errors_have_stable_codes_and_statuses()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();

        // Neither delta nor value → 400 adjustTarget.
        var neither = await client.GetAsync("/party/players/kira/adjust?counter=HP");
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        Assert.Equal("error.party.adjustTarget", Code(await neither.Content.ReadFromJsonAsync<JsonElement>()));

        // Both delta and value → 400 adjustTarget.
        var both = await client.GetAsync("/party/players/kira/adjust?counter=HP&delta=1&value=2");
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal("error.party.adjustTarget", Code(await both.Content.ReadFromJsonAsync<JsonElement>()));

        // Missing counter query → 400 adjustCounter.
        var noCounter = await client.GetAsync("/party/players/kira/adjust?delta=1");
        Assert.Equal(HttpStatusCode.BadRequest, noCounter.StatusCode);
        Assert.Equal("error.party.adjustCounter", Code(await noCounter.Content.ReadFromJsonAsync<JsonElement>()));

        // Unknown player → 404 playerNotFound.
        var noPlayer = await client.GetAsync("/party/players/ghost/adjust?counter=HP&delta=1");
        Assert.Equal(HttpStatusCode.NotFound, noPlayer.StatusCode);
        Assert.Equal("error.party.playerNotFound", Code(await noPlayer.Content.ReadFromJsonAsync<JsonElement>()));

        // Unknown counter → 404 counterNotFound.
        var noCounterLabel = await client.GetAsync("/party/players/kira/adjust?counter=Mana&delta=1");
        Assert.Equal(HttpStatusCode.NotFound, noCounterLabel.StatusCode);
        Assert.Equal("error.party.counterNotFound", Code(await noCounterLabel.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task Adjust_table_counter_works_and_reports_unknown_labels()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/party/counters", new object[] {
            new { label = "Fear", value = 2, max = 12 } })).EnsureSuccessStatusCode();

        var resp = await client.PostAsync("/party/counters/adjust?counter=Fear&delta=3", null);
        resp.EnsureSuccessStatusCode();
        var list = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, list[0].GetProperty("value").GetInt32());

        // Unknown table counter → 404 counterNotFound.
        var unknown = await client.GetAsync("/party/counters/adjust?counter=Doom&delta=1");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("error.party.counterNotFound", Code(await unknown.Content.ReadFromJsonAsync<JsonElement>()));
    }

    // ---- 5. Owned-portrait cleanup ----

    [Fact]
    public async Task Replacing_and_deleting_release_the_members_portrait()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var a = await UploadPngAsync(client);
        var b = await UploadPngAsync(client);

        (await client.PutAsJsonAsync("/party/players/kira", new { name = "Kira", portrait = a }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/images/{a}")).StatusCode);

        // Replace A with B → A's file is dropped, B's exists.
        (await client.PutAsJsonAsync("/party/players/kira", new { name = "Kira", portrait = b }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/images/{a}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/images/{b}")).StatusCode);

        // Deleting the member releases the remaining portrait.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/party/players/kira")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/images/{b}")).StatusCode);
    }

    // ---- 6. TV projection + the dynamic key-free portrait gate ----

    [Fact]
    public async Task Party_board_inlines_the_render_model_and_serves_portraits_key_free()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();
        var portrait = await UploadPngAsync(client, apiKey: Key);
        var unreferenced = await UploadPngAsync(client, apiKey: Key);

        (await PutJsonAsync(client, "/party/players/kira", new
        {
            name = "Kira",
            portrait,
            sortOrder = 0,
            counters = new object[] { new { label = "HP", value = 7, max = 10, style = "number" } },
        }, apiKey: Key)).EnsureSuccessStatusCode();
        (await PutJsonAsync(client, "/party/counters", new object[] {
            new { label = "Fear", value = 4, max = 12 } }, apiKey: Key)).EnsureSuccessStatusCode();

        (await PutJsonAsync(client, "/boards/roster", new
        {
            name = "Roster",
            elements = new object[] { new { kind = "party", x = 5.0, y = 5.0, w = 90.0, h = 90.0 } },
        }, apiKey: Key)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?board=roster&apiKey={Key}")).StatusCode);

        // /tv/state (key-free) inlines the party render model on the party element.
        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        var rev = Rev(state);
        var els = state.GetProperty("content").GetProperty("board").GetProperty("elements");
        var partyEl = els[0];
        Assert.Equal("party", partyEl.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, partyEl.GetProperty("url").ValueKind); // a party element carries no stream url

        var party = partyEl.GetProperty("party");
        var players = party.GetProperty("players");
        Assert.Equal(1, players.GetArrayLength());
        Assert.Equal("Kira", players[0].GetProperty("name").GetString());
        // The portrait ref is pre-resolved to the gate-validated per-name board route (with the rev cache-buster).
        Assert.Equal($"/tv/content/board/{portrait}?rev={rev}", players[0].GetProperty("portraitUrl").GetString());
        Assert.Equal(7, CounterValue(players[0], "HP"));
        Assert.Equal("number", players[0].GetProperty("counters")[0].GetProperty("style").GetString());
        Assert.Equal("Fear", party.GetProperty("counters")[0].GetProperty("label").GetString());
        Assert.Equal(4, party.GetProperty("counters")[0].GetProperty("value").GetInt32());

        // Key-free: the portrait streams through the board route (the dynamic party-portrait gate).
        var served = await client.GetAsync($"/tv/content/board/{portrait}");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);

        // Key-free: an uploaded-but-unreferenced image is still NOT served (the membership gate holds).
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tv/content/board/{unreferenced}")).StatusCode);
        // …and the general /images route stays locked for that same file.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/images/{unreferenced}")).StatusCode);
    }

    [Fact]
    public async Task Portrait_is_not_served_when_the_shown_board_has_no_party_element()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var portrait = await UploadPngAsync(client);

        (await client.PutAsJsonAsync("/party/players/kira", new { name = "Kira", portrait }))
            .EnsureSuccessStatusCode();
        // A plain board with no party element.
        (await client.PutAsJsonAsync("/boards/plain", new { name = "Plain" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=plain")).StatusCode);

        // The portrait exists on disk and belongs to a member, but the shown board doesn't render the party,
        // so the dynamic gate must NOT expose it.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tv/content/board/{portrait}")).StatusCode);
    }

    // ---- 7. The party rev-bump idiom ----

    [Fact]
    public async Task Party_changes_bump_rev_only_while_a_party_board_is_shown()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var img = await UploadPngAsync(client);

        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/boards/roster", new
        {
            name = "Roster",
            elements = new object[] { new { kind = "party", x = 1.0, y = 1.0, w = 90.0, h = 90.0 } },
        })).EnsureSuccessStatusCode();

        // While a party board is shown, an adjust bumps the rev.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=roster")).StatusCode);
        var rev1 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        (await client.GetAsync("/party/players/kira/adjust?counter=HP&delta=-1")).EnsureSuccessStatusCode();
        var rev2 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.True(rev2 > rev1, "adjust while a party board is shown should bump the rev");

        // A player upsert bumps too.
        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira the Bold",
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();
        var rev3 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.True(rev3 > rev2, "player upsert while a party board is shown should bump the rev");

        // A table-counter save bumps too.
        (await client.PutAsJsonAsync("/party/counters", new object[] { new { label = "Fear", value = 1 } }))
            .EnsureSuccessStatusCode();
        var rev4 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.True(rev4 > rev3, "table-counter save while a party board is shown should bump the rev");

        // Now show a plain IMAGE instead — a party adjust must NOT bump the rev (nothing party-shaped is live).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?image={img}")).StatusCode);
        var revImg = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        (await client.GetAsync("/party/players/kira/adjust?counter=HP&delta=-1")).EnsureSuccessStatusCode();
        var revImgAfter = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.Equal(revImg, revImgAfter);
    }
}
