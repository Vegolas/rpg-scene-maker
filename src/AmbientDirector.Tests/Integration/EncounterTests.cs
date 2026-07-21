using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// Encounters (issue #122): prepped fights (heroes + enemy instances + background + optional scene/event) the
/// GM runs to the TV. Covers the CRUD round-trip + 404s, owned-background cleanup, validation codes, running
/// (activates the scene + event best-effort and pushes the heroes-left / enemies-right view), per-instance live
/// tracking (adjust XOR/clamp/errors, reset), the /tv/state synthesis (heroes resolved from HeroIds; empty ⇒
/// all players; independent duplicate instances; live hero counters), the key-free portrait gate, and the
/// rev-bump idiom.
/// </summary>
[Collection("integration")]
public class EncounterTests
{
    private const string Key = "s3cret";

    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static async Task<string> UploadPngAsync(HttpClient client, string? apiKey = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Convert.FromBase64String(TinyPngBase64));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "art.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/images/upload") { Content = form };
        if (apiKey is not null) request.Headers.Add("X-Api-Key", apiKey);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
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

    // A minimal encounter body: one hero + two goblin instances (a duplicated statblock).
    private static object SampleEncounter(string bg = "", string? sceneId = null, string? eventId = null) => new
    {
        name = "Goblin Ambush",
        sortOrder = 0,
        heroIds = new[] { "kira" },
        backgroundImage = string.IsNullOrEmpty(bg) ? null : bg,
        activateSceneId = sceneId,
        activateEventId = eventId,
        enemies = new object[]
        {
            new
            {
                instanceId = "goblin-1",
                enemyId = "goblin",
                name = "Goblin 1",
                spotlight = true,
                counters = new object[] { new { label = "HP", value = 6, max = 6, style = "number" } },
            },
            new
            {
                instanceId = "goblin-2",
                enemyId = "goblin",
                name = "Goblin 2",
                counters = new object[] { new { label = "HP", value = 6, max = 6, style = "number" } },
            },
        },
    };

    // ---- 1. CRUD round-trip + ordering + no GET-single + owned-background cleanup ----

    [Fact]
    public async Task Put_list_delete_round_trip_and_404s()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/encounters/lich", new { name = "The Lich", sortOrder = 1 }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/encounters/goblins", SampleEncounter())).EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<JsonElement>("/encounters/list");
        Assert.Equal(2, list.GetArrayLength());
        // SortOrder ascending: goblins (0) before lich (1).
        Assert.Equal("goblins", list[0].GetProperty("id").GetString());
        Assert.Equal(2, list[0].GetProperty("enemies").GetArrayLength());
        Assert.Equal("goblin-1", list[0].GetProperty("enemies")[0].GetProperty("instanceId").GetString());
        Assert.True(list[0].GetProperty("enemies")[0].GetProperty("spotlight").GetBoolean());
        Assert.Equal(new[] { "kira" }, list[0].GetProperty("heroIds").EnumerateArray().Select(h => h.GetString()));

        // No GET /encounters/{id} API route — a full-page load falls through to the SPA host (not JSON).
        var single = await client.GetAsync("/encounters/goblins");
        Assert.NotEqual("application/json", single.Content.Headers.ContentType?.MediaType);

        // Delete removes it; deleting again is a 404.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/encounters/goblins")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/encounters/goblins")).StatusCode);
    }

    [Fact]
    public async Task Replacing_and_deleting_release_the_encounters_background()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var a = await UploadPngAsync(client);
        var b = await UploadPngAsync(client);

        (await client.PutAsJsonAsync("/encounters/goblins", SampleEncounter(bg: a))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/images/{a}")).StatusCode);

        // Replace A with B → A dropped, B kept.
        (await client.PutAsJsonAsync("/encounters/goblins", SampleEncounter(bg: b))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/images/{a}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/images/{b}")).StatusCode);

        // Delete releases the remaining background.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/encounters/goblins")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/images/{b}")).StatusCode);
    }

    [Fact]
    public async Task Invalid_encounters_are_rejected_with_stable_codes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        async Task AssertCode(string id, object body, string expectedCode)
        {
            var resp = await client.PutAsJsonAsync($"/encounters/{id}", body);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal(expectedCode, Code(await resp.Content.ReadFromJsonAsync<JsonElement>()));
        }

        await AssertCode("nameless", new { name = "" }, "error.common.nameRequired");
        await AssertCode("badhero", new { name = "X", heroIds = new[] { "bad id" } }, "error.encounter.heroId");
        await AssertCode("dupe", new
        {
            name = "X",
            enemies = new object[]
            {
                new { instanceId = "g1", enemyId = "goblin", name = "G1" },
                new { instanceId = "g1", enemyId = "goblin", name = "G2" },
            },
        }, "error.encounter.duplicateInstance");
    }

    // ---- 2. Running: activates the scene + event best-effort and shows on the TV ----

    [Fact]
    public async Task Run_activates_the_scene_and_event_and_shows_the_encounter()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A bare scene + event (no light/music/sound) so activation fully succeeds under the unconfigured
        // test lighting — this proves the run WIRING, not the integrations.
        (await client.PutAsJsonAsync("/scenes/battle", new { name = "Battle" })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/events/war-cry", new { name = "War Cry" })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/encounters/goblins",
            SampleEncounter(sceneId: "battle", eventId: "war-cry"))).EnsureSuccessStatusCode();

        var run = await client.GetAsync("/encounters/goblins/run");
        run.EnsureSuccessStatusCode();
        var body = await run.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("scene").GetString());
        Assert.Equal("ok", body.GetProperty("event").GetString());
        Assert.Equal("goblins", body.GetProperty("encounter").GetString());

        // The scene really activated (CurrentState) — proof the run reached SceneActivator.
        var active = await client.GetFromJsonAsync<JsonElement>("/scenes/active");
        Assert.Equal("battle", active.GetProperty("id").GetString());

        // …and the encounter is on the TV (kind "board" on the wire so BoardCanvas draws it unchanged).
        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        Assert.Equal("board", state.GetProperty("content").GetProperty("kind").GetString());
        var els = state.GetProperty("content").GetProperty("board").GetProperty("elements");
        Assert.Equal(3, els.GetArrayLength()); // heroes + enemies + fear (issue #144 added the fear element)
    }

    [Fact]
    public async Task Run_reports_skipped_scene_event_and_notfound_for_missing_refs()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // No scene/event configured → both "skipped"; still shows.
        (await client.PutAsJsonAsync("/encounters/none", SampleEncounter())).EnsureSuccessStatusCode();
        var run = await (await client.GetAsync("/encounters/none/run")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("skipped", run.GetProperty("scene").GetString());
        Assert.Equal("skipped", run.GetProperty("event").GetString());

        // Configured but the refs don't exist → "notFound" (best-effort — the show still happens).
        (await client.PutAsJsonAsync("/encounters/ghosts",
            SampleEncounter(sceneId: "no-scene", eventId: "no-event"))).EnsureSuccessStatusCode();
        var run2 = await (await client.PostAsync("/encounters/ghosts/run", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("notFound", run2.GetProperty("scene").GetString());
        Assert.Equal("notFound", run2.GetProperty("event").GetString());

        // Running a missing encounter is a 404.
        var missing = await client.GetAsync("/encounters/ghost/run");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("error.encounter.notFound", Code(await missing.Content.ReadFromJsonAsync<JsonElement>()));
    }

    // ---- 3. Per-instance live tracking: adjust (XOR/clamp/errors) + reset ----

    [Fact]
    public async Task Adjust_enemy_instance_counter_independently_with_xor_and_clamp()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/encounters/goblins", SampleEncounter())).EnsureSuccessStatusCode();

        // Damage goblin-1; goblin-2 (same statblock) is untouched — independent tracking.
        var adj = await client.GetAsync("/encounters/goblins/enemies/goblin-1/adjust?counter=HP&delta=-4");
        adj.EnsureSuccessStatusCode();
        var updated = await adj.Content.ReadFromJsonAsync<JsonElement>();
        var instances = updated.GetProperty("enemies");
        Assert.Equal(2, CounterValue(instances[0], "HP"));
        Assert.Equal(6, CounterValue(instances[1], "HP"));

        // Clamp at 0 (POST form).
        (await client.PostAsync("/encounters/goblins/enemies/goblin-1/adjust?counter=HP&delta=-999", null))
            .EnsureSuccessStatusCode();
        // Value set absolutely, clamped to Max.
        var toMax = await (await client.GetAsync("/encounters/goblins/enemies/goblin-1/adjust?counter=HP&value=99"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, CounterValue(toMax.GetProperty("enemies")[0], "HP"));

        // XOR: neither → 400; unknown instance → 404; unknown counter → 404.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/encounters/goblins/enemies/goblin-1/adjust?counter=HP")).StatusCode);
        var noInstance = await client.GetAsync("/encounters/goblins/enemies/ghost/adjust?counter=HP&delta=1");
        Assert.Equal(HttpStatusCode.NotFound, noInstance.StatusCode);
        Assert.Equal("error.encounter.enemyInstanceNotFound", Code(await noInstance.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task Reset_reseeds_instances_from_the_bestiary_starting_values()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A bestiary template that starts fresh at 0 (HP/Stress tracked upward as damage/stress is marked).
        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            counters = new object[]
            {
                new { label = "HP", value = 0, max = 8 },
                new { label = "Stress", value = 0, max = 4 },
            },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/encounters/fight", new
        {
            name = "Fight",
            enemies = new object[]
            {
                new { instanceId = "g1", enemyId = "goblin", name = "Goblin 1", counters = new object[]
                    { new { label = "HP", value = 0, max = 8 }, new { label = "Stress", value = 0, max = 4 } } },
            },
        })).EnsureSuccessStatusCode();

        // Mark damage on the instance.
        (await client.GetAsync("/encounters/fight/enemies/g1/adjust?counter=HP&value=5")).EnsureSuccessStatusCode();
        (await client.GetAsync("/encounters/fight/enemies/g1/adjust?counter=Stress&value=3")).EnsureSuccessStatusCode();

        // Reset → back to the statblock's fresh (0) values, not to Max.
        var reset = await (await client.PostAsync("/encounters/fight/reset", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, CounterValue(reset.GetProperty("enemies")[0], "HP"));
        Assert.Equal(0, CounterValue(reset.GetProperty("enemies")[0], "Stress"));
    }

    // ---- 4. TV synthesis: heroes-left / enemies-right, live hero counters, HeroIds resolution ----

    [Fact]
    public async Task Tv_state_synthesizes_the_heroes_left_enemies_right_view()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Two party members with persistent counters; Fear on the table; only kira is in the encounter.
        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira", sortOrder = 0,
            counters = new object[] { new { label = "HP", value = 7, max = 10, style = "number" } },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/players/aldous", new { name = "Aldous", sortOrder = 1 }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/counters", new object[] {
            new { label = "Fear", value = 3, max = 12 } })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync("/encounters/goblins", SampleEncounter())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?encounter=goblins")).StatusCode);

        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        var els = state.GetProperty("content").GetProperty("board").GetProperty("elements");
        Assert.Equal(3, els.GetArrayLength()); // heroes-left, enemies-right, fear top-centre (issue #144)
        Assert.Equal("party", els[0].GetProperty("kind").GetString());
        Assert.Equal("enemies", els[1].GetProperty("kind").GetString());
        // The fear element (issue #144) shares the same render model; on the wire it carries the roster too.
        Assert.Equal("fear", els[2].GetProperty("kind").GetString());

        var party = els[0].GetProperty("party");
        // Only kira (the chosen hero), NOT aldous; and her persistent party HP shows through.
        var players = party.GetProperty("players");
        Assert.Equal(1, players.GetArrayLength());
        Assert.Equal("Kira", players[0].GetProperty("name").GetString());
        Assert.Equal(7, CounterValue(players[0], "HP"));
        // Table Fear rides on the party element's counters.
        Assert.Equal("Fear", party.GetProperty("counters")[0].GetProperty("label").GetString());

        // Enemies (both instances) ride on BOTH elements' shared render model.
        var enemies = els[1].GetProperty("party").GetProperty("enemies");
        Assert.Equal(2, enemies.GetArrayLength());
        Assert.Equal("Goblin 1", enemies[0].GetProperty("name").GetString());
        Assert.True(enemies[0].GetProperty("spotlight").GetBoolean());
        Assert.False(enemies[1].GetProperty("spotlight").GetBoolean());
    }

    [Fact]
    public async Task Empty_hero_ids_means_all_current_players()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/party/players/kira", new { name = "Kira", sortOrder = 0 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/players/aldous", new { name = "Aldous", sortOrder = 1 })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync("/encounters/all", new { name = "Everyone", heroIds = Array.Empty<string>() }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?encounter=all")).StatusCode);

        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        var players = state.GetProperty("content").GetProperty("board").GetProperty("elements")[0]
            .GetProperty("party").GetProperty("players");
        Assert.Equal(2, players.GetArrayLength()); // both players, empty selection = all
    }

    [Fact]
    public async Task Hidden_instances_are_skipped_on_the_tv_but_kept_in_the_encounter()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/encounters/fight", new
        {
            name = "Fight",
            enemies = new object[]
            {
                new { instanceId = "g1", enemyId = "goblin", name = "Goblin 1",
                    counters = new object[] { new { label = "HP", value = 0, max = 6 } } },
                new { instanceId = "g2", enemyId = "goblin", name = "Goblin 2 (held back)", hidden = true,
                    counters = new object[] { new { label = "HP", value = 0, max = 6 } } },
            },
        })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?encounter=fight")).StatusCode);

        // The TV shows only the visible instance…
        var state = await client.GetFromJsonAsync<JsonElement>("/tv/state");
        var enemies = state.GetProperty("content").GetProperty("board").GetProperty("elements")[1]
            .GetProperty("party").GetProperty("enemies");
        Assert.Equal(1, enemies.GetArrayLength());
        Assert.Equal("Goblin 1", enemies[0].GetProperty("name").GetString());

        // …but both are still kept in the encounter (the hidden one can be revealed later).
        var list = await client.GetFromJsonAsync<JsonElement>("/encounters/list");
        var stored = list.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "fight");
        Assert.Equal(2, stored.GetProperty("enemies").GetArrayLength());
        var hidden = stored.GetProperty("enemies").EnumerateArray().Single(e => e.GetProperty("instanceId").GetString() == "g2");
        Assert.True(hidden.GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public async Task Instances_default_to_shown_when_the_field_is_omitted()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        // No "hidden" on the wire → defaults false, so the instance is shown.
        (await client.PutAsJsonAsync("/encounters/fight", new
        {
            name = "Fight",
            enemies = new object[] { new { instanceId = "g1", enemyId = "goblin", name = "Goblin" } },
        })).EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<JsonElement>("/encounters/list");
        var stored = list.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "fight");
        Assert.False(stored.GetProperty("enemies")[0].GetProperty("hidden").GetBoolean());
    }

    // ---- 5. The key-free portrait gate: serves the encounter's files only while it is shown ----

    [Fact]
    public async Task Portrait_gate_serves_encounter_files_only_while_it_is_shown()
    {
        using var factory = new ApiFactory(apiKey: Key);
        var client = factory.CreateClient();
        var background = await UploadPngAsync(client, Key);
        var heroPortrait = await UploadPngAsync(client, Key);
        var enemyPortrait = await UploadPngAsync(client, Key);
        var unreferenced = await UploadPngAsync(client, Key);

        (await PutJsonAsync(client, "/party/players/kira",
            new { name = "Kira", portrait = heroPortrait }, Key)).EnsureSuccessStatusCode();
        (await PutJsonAsync(client, "/encounters/goblins", new
        {
            name = "Goblin Ambush",
            heroIds = new[] { "kira" },
            backgroundImage = background,
            enemies = new object[]
            {
                new { instanceId = "goblin-1", enemyId = "goblin", name = "Goblin 1", portrait = enemyPortrait,
                    counters = new object[] { new { label = "HP", value = 6, max = 6 } } },
            },
        }, Key)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/show?encounter=goblins&apiKey={Key}")).StatusCode);

        // Key-free: background + hero portrait + enemy instance portrait all stream through the board route.
        foreach (var name in new[] { background, heroPortrait, enemyPortrait })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/content/board/{name}")).StatusCode);

        // An uploaded-but-unreferenced image is NOT served (membership gate holds), and /images stays locked.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tv/content/board/{unreferenced}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/images/{unreferenced}")).StatusCode);

        // Clear the display → the same files are no longer served (nothing shown).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/tv/clear?apiKey={Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tv/content/board/{enemyPortrait}")).StatusCode);
    }

    // ---- 6. rev-bump gating ----

    [Fact]
    public async Task Changes_bump_rev_while_the_encounter_is_shown_per_the_snapshot_rules()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            counters = new object[] { new { label = "HP", value = 5, max = 10 } },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            counters = new object[] { new { label = "HP", value = 6, max = 6 } },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/encounters/goblins", SampleEncounter())).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tv/show?encounter=goblins")).StatusCode);
        async Task<long> Now() => Rev(await client.GetFromJsonAsync<JsonElement>("/tv/state"));

        // A per-instance adjust bumps.
        var r0 = await Now();
        (await client.GetAsync("/encounters/goblins/enemies/goblin-1/adjust?counter=HP&delta=-1")).EnsureSuccessStatusCode();
        var r1 = await Now();
        Assert.True(r1 > r0, "instance adjust while the encounter is shown should bump the rev");

        // A hero (party player) change bumps — the encounter's hero panel is live.
        (await client.GetAsync("/party/players/kira/adjust?counter=HP&delta=-1")).EnsureSuccessStatusCode();
        var r2 = await Now();
        Assert.True(r2 > r1, "hero change while the encounter is shown should bump the rev");

        // A table-counter (Fear) save bumps too.
        (await client.PutAsJsonAsync("/party/counters", new object[] { new { label = "Fear", value = 1 } }))
            .EnsureSuccessStatusCode();
        var r3 = await Now();
        Assert.True(r3 > r2, "table-counter change while the encounter is shown should bump the rev");

        // A live edit (PUT) of the shown encounter bumps.
        (await client.PutAsJsonAsync("/encounters/goblins", SampleEncounter())).EnsureSuccessStatusCode();
        var r4 = await Now();
        Assert.True(r4 > r3, "editing the shown encounter should bump the rev");

        // But editing a bestiary TEMPLATE does NOT bump a shown encounter (instances are snapshots).
        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin Chief",
            counters = new object[] { new { label = "HP", value = 6, max = 6 } },
        })).EnsureSuccessStatusCode();
        Assert.Equal(r4, await Now());

        // A different, not-shown encounter's adjust does not bump.
        (await client.PutAsJsonAsync("/encounters/other", SampleEncounter())).EnsureSuccessStatusCode();
        var rOther = await Now();
        (await client.GetAsync("/encounters/other/enemies/goblin-1/adjust?counter=HP&delta=-1")).EnsureSuccessStatusCode();
        Assert.Equal(rOther, await Now());
    }

    // ---- 7. /tv/show exactly-one XOR now covers ?encounter= ----

    [Fact]
    public async Task Show_requires_exactly_one_target()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        (await client.PutAsJsonAsync("/encounters/goblins", SampleEncounter())).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/boards/plain", new { name = "Plain" })).EnsureSuccessStatusCode();

        // Two targets → 400 showTarget.
        var both = await client.GetAsync("/tv/show?board=plain&encounter=goblins");
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal("error.tv.showTarget", Code(await both.Content.ReadFromJsonAsync<JsonElement>()));

        // Unknown encounter → 400 encounterNotFound.
        var unknown = await client.GetAsync("/tv/show?encounter=ghost");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("error.tv.encounterNotFound", Code(await unknown.Content.ReadFromJsonAsync<JsonElement>()));
    }
}
