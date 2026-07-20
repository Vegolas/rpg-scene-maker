using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// The encounter's enemy roster (issue #120): the players' twin in the party domain. Covers the CRUD
/// round-trip + ordering, the /party/list envelope, validation codes, the spotlight flag, tap-to-adjust
/// (delta/value, GET + POST, clamping, error codes) and — most importantly — that a board's kind="enemies"
/// element drives the live TV render + rev-bump exactly like kind="party", and that the two coexist on one
/// encounter board.
/// </summary>
[Collection("integration")]
public class EnemyTests
{
    private static string? Code(JsonElement problem) => problem.GetProperty("code").GetString();
    private static long Rev(JsonElement state) => state.GetProperty("rev").GetInt64();

    private static int CounterValue(JsonElement owner, string label) =>
        owner.GetProperty("counters").EnumerateArray()
            .Single(c => c.GetProperty("label").GetString() == label).GetProperty("value").GetInt32();

    // ---- 1. CRUD round-trip + ordering + the /party/list envelope ----

    [Fact]
    public async Task Put_enemies_round_trip_ordered_by_sortorder_and_delete_removes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Two enemies inserted out of roster order; SortOrder (ties → Id) decides the order.
        (await client.PutAsJsonAsync("/party/enemies/dread-king", new
        {
            name = "Dread King",
            spotlight = true,
            sortOrder = 1,
            counters = new object[]
            {
                new { label = "HP", value = 8, max = 8, style = "number" },
                new { label = "Stress", value = 0, max = 4, style = "pips" },
            },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/enemies/goblin", new { name = "Goblin", sortOrder = 0 }))
            .EnsureSuccessStatusCode();

        var party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        var enemies = party.GetProperty("enemies");
        Assert.Equal(2, enemies.GetArrayLength());
        // Ordered by SortOrder: goblin (0) before dread-king (1).
        Assert.Equal("goblin", enemies[0].GetProperty("id").GetString());
        Assert.Equal("dread-king", enemies[1].GetProperty("id").GetString());

        var boss = enemies[1];
        Assert.Equal("Dread King", boss.GetProperty("name").GetString());
        Assert.True(boss.GetProperty("spotlight").GetBoolean());
        var counters = boss.GetProperty("counters");
        Assert.Equal(2, counters.GetArrayLength());
        Assert.Equal("HP", counters[0].GetProperty("label").GetString());
        Assert.Equal(8, counters[0].GetProperty("value").GetInt32());
        Assert.Equal("number", counters[0].GetProperty("style").GetString());

        // The players + table counters live in the same envelope and are unaffected (empty here).
        Assert.Equal(0, party.GetProperty("players").GetArrayLength());

        // Delete removes the enemy.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/party/enemies/goblin")).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/party/list");
        Assert.DoesNotContain(after.GetProperty("enemies").EnumerateArray(),
            e => e.GetProperty("id").GetString() == "goblin");

        // Deleting a missing enemy is a 404.
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/party/enemies/goblin")).StatusCode);
    }

    [Fact]
    public async Task Get_one_enemy_is_not_an_api_route_so_full_page_loads_reach_the_spa()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/party/enemies/goblin", new { name = "Goblin" })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/party/enemies/goblin");

        // There is deliberately no GET /party/enemies/{id} API route (the panel reads /party/list and picks by
        // id), so a full-page GET must NOT return JSON — it falls through to the SPA host instead.
        Assert.NotEqual("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- 2. Validation → 400 with stable codes (shared PartyValidation) ----

    [Fact]
    public async Task Invalid_enemies_are_rejected_with_stable_codes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        async Task AssertCode(string id, object body, string expectedCode)
        {
            var resp = await client.PutAsJsonAsync($"/party/enemies/{id}", body);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal(expectedCode, Code(await resp.Content.ReadFromJsonAsync<JsonElement>()));
        }

        // Bad slug id (the space is not a slug char).
        await AssertCode("bad%20name", new { name = "X" }, "error.common.idSlug");
        // Missing name.
        await AssertCode("nameless", new { name = "" }, "error.common.nameRequired");
        // Bad counter style (shared counter guard).
        await AssertCode("goblin", new { name = "Goblin", counters = new object[] {
            new { label = "HP", value = 1, style = "bars" } } }, "error.party.counterStyle");
        // Pips without a max (shared counter guard).
        await AssertCode("goblin", new { name = "Goblin", counters = new object[] {
            new { label = "HP", value = 1, style = "pips" } } }, "error.party.pipsMax");
    }

    [Fact]
    public async Task Put_clamps_enemy_counter_value_into_range()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var high = await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            counters = new object[] { new { label = "HP", value = 999, max = 4 } },
        });
        high.EnsureSuccessStatusCode();
        Assert.Equal(4, CounterValue(await high.Content.ReadFromJsonAsync<JsonElement>(), "HP"));
    }

    // ---- 3. Tap-to-adjust: delta / value, GET + POST, clamping, error codes ----

    [Fact]
    public async Task Adjust_enemy_counter_by_delta_and_value_via_get_and_post_with_clamping()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();

        // GET delta decrements (Stream Deck style: -1 boss HP).
        var dec = await client.GetAsync("/party/enemies/goblin/adjust?counter=HP&delta=-2");
        dec.EnsureSuccessStatusCode();
        Assert.Equal(3, CounterValue(await dec.Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // POST delta increments.
        var inc = await client.PostAsync("/party/enemies/goblin/adjust?counter=HP&delta=4", null);
        inc.EnsureSuccessStatusCode();
        Assert.Equal(7, CounterValue(await inc.Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // Delta clamps at Max and at 0.
        Assert.Equal(10, CounterValue(await (await client.GetAsync("/party/enemies/goblin/adjust?counter=HP&delta=999")).Content.ReadFromJsonAsync<JsonElement>(), "HP"));
        Assert.Equal(0, CounterValue(await (await client.GetAsync("/party/enemies/goblin/adjust?counter=HP&delta=-999")).Content.ReadFromJsonAsync<JsonElement>(), "HP"));

        // Value sets absolutely; label matched case-insensitively.
        Assert.Equal(8, CounterValue(await (await client.GetAsync("/party/enemies/goblin/adjust?counter=hp&value=8")).Content.ReadFromJsonAsync<JsonElement>(), "HP"));
    }

    [Fact]
    public async Task Adjust_errors_have_stable_codes_and_statuses()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();

        // Neither delta nor value → 400 adjustTarget.
        var neither = await client.GetAsync("/party/enemies/goblin/adjust?counter=HP");
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        Assert.Equal("error.party.adjustTarget", Code(await neither.Content.ReadFromJsonAsync<JsonElement>()));

        // Both delta and value → 400 adjustTarget.
        var both = await client.GetAsync("/party/enemies/goblin/adjust?counter=HP&delta=1&value=2");
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal("error.party.adjustTarget", Code(await both.Content.ReadFromJsonAsync<JsonElement>()));

        // Missing counter query → 400 adjustCounter.
        var noCounter = await client.GetAsync("/party/enemies/goblin/adjust?delta=1");
        Assert.Equal(HttpStatusCode.BadRequest, noCounter.StatusCode);
        Assert.Equal("error.party.adjustCounter", Code(await noCounter.Content.ReadFromJsonAsync<JsonElement>()));

        // Unknown enemy → 404 enemyNotFound (the new code).
        var noEnemy = await client.GetAsync("/party/enemies/ghost/adjust?counter=HP&delta=1");
        Assert.Equal(HttpStatusCode.NotFound, noEnemy.StatusCode);
        Assert.Equal("error.party.enemyNotFound", Code(await noEnemy.Content.ReadFromJsonAsync<JsonElement>()));

        // Unknown counter → 404 counterNotFound (shared with members).
        var noLabel = await client.GetAsync("/party/enemies/goblin/adjust?counter=Mana&delta=1");
        Assert.Equal(HttpStatusCode.NotFound, noLabel.StatusCode);
        Assert.Equal("error.party.counterNotFound", Code(await noLabel.Content.ReadFromJsonAsync<JsonElement>()));
    }

    // ---- 4. TV projection: an enemies element inlines the live enemy roster (both kinds coexist) ----

    [Fact]
    public async Task Encounter_board_inlines_the_enemy_roster_on_both_party_and_enemies_elements()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/party/players/kira", new { name = "Kira", sortOrder = 0 }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/enemies/dread-king", new
        {
            name = "Dread King",
            spotlight = true,
            sortOrder = 0,
            counters = new object[] { new { label = "HP", value = 6, max = 8, style = "number" } },
        })).EnsureSuccessStatusCode();

        // An encounter board with BOTH a party element and an enemies element.
        (await client.PutAsJsonAsync("/boards/encounter", new
        {
            name = "Encounter",
            elements = new object[]
            {
                new { kind = "party", x = 0.5, y = 2.0, w = 31.0, h = 96.0 },
                new { kind = "enemies", x = 75.5, y = 2.0, w = 24.0, h = 96.0 },
            },
        })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=encounter")).StatusCode);

        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        var els = state.GetProperty("content").GetProperty("board").GetProperty("elements");
        Assert.Equal(2, els.GetArrayLength());

        // Both live-roster elements carry the same render model — each exposes players AND enemies.
        foreach (var el in els.EnumerateArray())
        {
            var kind = el.GetProperty("kind").GetString();
            Assert.True(kind is "party" or "enemies");
            var party = el.GetProperty("party");
            Assert.Equal(JsonValueKind.Null, el.GetProperty("url").ValueKind); // neither carries a stream url

            Assert.Equal("Kira", party.GetProperty("players")[0].GetProperty("name").GetString());

            var enemies = party.GetProperty("enemies");
            Assert.Equal(1, enemies.GetArrayLength());
            Assert.Equal("Dread King", enemies[0].GetProperty("name").GetString());
            Assert.True(enemies[0].GetProperty("spotlight").GetBoolean());
            Assert.Equal(6, CounterValue(enemies[0], "HP"));
        }
    }

    // ---- 5. The rev-bump idiom extends to enemies + enemies-only boards ----

    [Fact]
    public async Task Enemy_changes_bump_rev_while_an_enemies_board_is_shown_and_not_otherwise()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();
        // A board with ONLY an enemies element (no party element).
        (await client.PutAsJsonAsync("/boards/enemies-only", new
        {
            name = "Enemies",
            elements = new object[] { new { kind = "enemies", x = 1.0, y = 1.0, w = 90.0, h = 90.0 } },
        })).EnsureSuccessStatusCode();
        // A plain board with neither a party nor an enemies element.
        (await client.PutAsJsonAsync("/boards/plain", new { name = "Plain" })).EnsureSuccessStatusCode();

        // While an enemies board is shown, an enemy adjust bumps the rev.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=enemies-only")).StatusCode);
        var rev1 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        (await client.GetAsync("/party/enemies/goblin/adjust?counter=HP&delta=-1")).EnsureSuccessStatusCode();
        var rev2 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.True(rev2 > rev1, "adjust while an enemies board is shown should bump the rev");

        // An enemy upsert (e.g. spotlight toggle — the UI sends the whole enemy, counters included) bumps too.
        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            spotlight = true,
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();
        var rev3 = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.True(rev3 > rev2, "enemy upsert while an enemies board is shown should bump the rev");

        // Now show a board with NEITHER live-roster element — an enemy adjust must NOT bump the rev.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?board=plain")).StatusCode);
        var revPlain = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        (await client.GetAsync("/party/enemies/goblin/adjust?counter=HP&delta=-1")).EnsureSuccessStatusCode();
        var revPlainAfter = Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));
        Assert.Equal(revPlain, revPlainAfter);
    }
}
