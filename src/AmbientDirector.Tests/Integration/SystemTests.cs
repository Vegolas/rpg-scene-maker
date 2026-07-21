using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// The game-system setting over the wire (issue #127): /systems/list + /systems/current (validation codes,
/// the "none" sentinel, GET+POST), the table-counter seeding (append + adopt-by-label, localized via
/// X-Ui-Lang), adjust-by-key vs by-label on every adjust route, PartyDto.System, and the API-key gate on the
/// new prefix. The startup one-shot's DB-level behavior is GameSystemUpgradeTests; here the app boots with an
/// empty DB, so the upgrade is a no-op and Current starts null.
/// </summary>
[Collection("integration")]
public class SystemTests
{
    private static string? Code(JsonElement problem) => problem.GetProperty("code").GetString();

    private static JsonElement CounterByKey(JsonElement counters, string key) =>
        counters.EnumerateArray().Single(c =>
            c.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String && k.GetString() == key);

    // Render-model counters (TvPartyCounterDto) carry no key on the wire — find them by their (localized) label.
    private static JsonElement CounterByLabel(JsonElement counters, string label) =>
        counters.EnumerateArray().Single(c => c.GetProperty("label").GetString() == label);

    // Assert a render counter resolved to the expected curated glyph name + content colour (null = JSON null).
    private static void AssertGlyph(JsonElement counter, string? glyph, string? color)
    {
        if (glyph is null)
            Assert.Equal(JsonValueKind.Null, counter.GetProperty("glyph").ValueKind);
        else
            Assert.Equal(glyph, counter.GetProperty("glyph").GetString());
        if (color is null)
            Assert.Equal(JsonValueKind.Null, counter.GetProperty("color").ValueKind);
        else
            Assert.Equal(color, counter.GetProperty("color").GetString());
    }

    [Fact]
    public async Task List_returns_daggerheart_definition_and_null_current_on_a_fresh_install()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var dto = await client.GetFromJsonAsync<JsonElement>("/systems/list");

        Assert.Equal(JsonValueKind.Null, dto.GetProperty("current").ValueKind);
        var daggerheart = dto.GetProperty("systems").EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == "daggerheart");
        Assert.Equal("system.daggerheart.name", daggerheart.GetProperty("nameKey").GetString());
        Assert.Equal(4, daggerheart.GetProperty("memberCounters").GetArrayLength());
        Assert.Equal(2, daggerheart.GetProperty("enemyCounters").GetArrayLength());
        Assert.Equal("fear", daggerheart.GetProperty("quickbar").EnumerateArray().Single().GetString());
        Assert.Equal("SPOTLIGHT", daggerheart.GetProperty("spotlightLabel").GetString());

        // The fear preset carries the render/table seed data phase 2/3 build on.
        var fear = CounterByKey(daggerheart.GetProperty("tableCounters"), "fear");
        Assert.Equal("party.preset.fear", fear.GetProperty("labelKey").GetString());
        Assert.Equal(12, fear.GetProperty("max").GetInt32());
        Assert.Equal("pips", fear.GetProperty("style").GetString());
    }

    [Fact]
    public async Task Selecting_daggerheart_seeds_fear_localized_and_selecting_none_clears_without_deleting()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Select over GET (the Stream Deck path) with a Polish panel language.
        using var select = new HttpRequestMessage(HttpMethod.Get, "/systems/current?id=daggerheart");
        select.Headers.Add("X-Ui-Lang", "pl");
        var selected = await client.SendAsync(select);
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        Assert.Equal("daggerheart",
            (await selected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("current").GetString());

        // Seeded table counter: the fear KEY with the POLISH label, at the preset's start/max/style.
        var party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        Assert.Equal("daggerheart", party.GetProperty("system").GetString());
        var fear = CounterByKey(party.GetProperty("counters"), "fear");
        Assert.Equal("Strach", fear.GetProperty("label").GetString());
        Assert.Equal(0, fear.GetProperty("value").GetInt32());
        Assert.Equal(12, fear.GetProperty("max").GetInt32());

        // Adjust by KEY (locale-independent) and by the localized LABEL — both must hit the same counter.
        (await client.PostAsync("/party/counters/adjust?counter=fear&delta=2", null)).EnsureSuccessStatusCode();
        (await client.PostAsync("/party/counters/adjust?counter=Strach&delta=1", null)).EnsureSuccessStatusCode();
        party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        Assert.Equal(3, CounterByKey(party.GetProperty("counters"), "fear").GetProperty("value").GetInt32());

        // "none" clears the selection but deletes nothing; re-selecting must not duplicate the counter.
        (await client.PostAsync("/systems/current?id=none", null)).EnsureSuccessStatusCode();
        var systems = await client.GetFromJsonAsync<JsonElement>("/systems/list");
        Assert.Equal(JsonValueKind.Null, systems.GetProperty("current").ValueKind);
        party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        Assert.Equal(JsonValueKind.Null, party.GetProperty("system").ValueKind);
        Assert.Equal(1, party.GetProperty("counters").GetArrayLength());

        (await client.PostAsync("/systems/current?id=daggerheart", null)).EnsureSuccessStatusCode();
        party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        Assert.Equal(1, party.GetProperty("counters").GetArrayLength());
        Assert.Equal(3, CounterByKey(party.GetProperty("counters"), "fear").GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Selecting_adopts_a_hand_made_counter_by_label_instead_of_duplicating()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A pre-#127 style table counter: the English preset label, hand-tuned value, no key.
        (await client.PutAsJsonAsync("/party/counters", new object[]
        {
            new { label = "Fear", value = 7, max = 12, style = "number" },
        })).EnsureSuccessStatusCode();

        (await client.PostAsync("/systems/current?id=daggerheart", null)).EnsureSuccessStatusCode();

        var party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        var counters = party.GetProperty("counters");
        Assert.Equal(1, counters.GetArrayLength()); // adopted, not duplicated
        var fear = CounterByKey(counters, "fear");
        Assert.Equal("Fear", fear.GetProperty("label").GetString());
        Assert.Equal(7, fear.GetProperty("value").GetInt32());          // the GM's value survives
        Assert.Equal("number", fear.GetProperty("style").GetString());  // and their style choice too
    }

    [Fact]
    public async Task Unknown_or_missing_ids_are_rejected_with_stable_codes()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var unknown = await client.PostAsync("/systems/current?id=call-of-cthulhu", null);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("error.system.unknown", Code(await unknown.Content.ReadFromJsonAsync<JsonElement>()));

        var missing = await client.PostAsync("/systems/current", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("error.system.idRequired", Code(await missing.Content.ReadFromJsonAsync<JsonElement>()));

        // Nothing was stored by the failed attempts.
        var dto = await client.GetFromJsonAsync<JsonElement>("/systems/list");
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("current").ValueKind);
    }

    [Fact]
    public async Task Player_and_enemy_adjusts_resolve_keys_and_keys_round_trip_through_saves()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // A player + bestiary enemy whose counters carry keys (as the phase-3 presets will stamp them).
        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            counters = new object[] { new { label = "Życie", key = "HP", value = 2, max = 6, style = "pips" } },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            counters = new object[] { new { label = "Stres", key = "stress", value = 0, max = 3, style = "pips" } },
        })).EnsureSuccessStatusCode();

        // Keys normalize to lowercase on save and resolve case-insensitively on adjust.
        var adjusted = await client.PostAsync("/party/players/kira/adjust?counter=hp&delta=3", null);
        Assert.Equal(HttpStatusCode.OK, adjusted.StatusCode);
        var kira = await adjusted.Content.ReadFromJsonAsync<JsonElement>();
        var hp = CounterByKey(kira.GetProperty("counters"), "hp");
        Assert.Equal("Życie", hp.GetProperty("label").GetString());
        Assert.Equal(5, hp.GetProperty("value").GetInt32());

        (await client.PostAsync("/party/enemies/goblin/adjust?counter=STRESS&value=2", null)).EnsureSuccessStatusCode();
        var party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        var goblin = party.GetProperty("enemies").EnumerateArray().Single();
        Assert.Equal(2, CounterByKey(goblin.GetProperty("counters"), "stress").GetProperty("value").GetInt32());

        // A duplicate key is a 400 with its own code (labels differ, so the label guard can't catch it).
        var dupe = await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            counters = new object[]
            {
                new { label = "Życie", key = "hp", value = 2, max = 6, style = "pips" },
                new { label = "Health", key = "hp", value = 1, max = 6, style = "pips" },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, dupe.StatusCode);
        Assert.Equal("error.party.duplicateCounterKey", Code(await dupe.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task Systems_routes_sit_behind_the_api_key_gate()
    {
        using var factory = new ApiFactory(apiKey: "s3cret");
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/systems/list")).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/systems/list");
        request.Headers.Add("X-Api-Key", "s3cret");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    // ---- issue #129: the D&D 5e sample proves the contract is not Daggerheart-shaped — number-style presets,
    // no table counters, empty quickbar, no spotlight label ----

    [Fact]
    public async Task Dnd5e_sample_lists_with_number_presets_and_seeds_no_table_counters()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<JsonElement>("/systems/list");
        var dnd = list.GetProperty("systems").EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == "dnd5e");
        Assert.Equal("system.dnd5e.name", dnd.GetProperty("nameKey").GetString());
        Assert.Empty(dnd.GetProperty("tableCounters").EnumerateArray());
        Assert.Empty(dnd.GetProperty("quickbar").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, dnd.GetProperty("spotlightLabel").ValueKind);

        // Members: HP + AC, both the "number" style (not pips) — the point of the non-Daggerheart sample.
        var members = dnd.GetProperty("memberCounters").EnumerateArray().ToList();
        Assert.Equal(new[] { "hp", "ac" }, members.Select(m => m.GetProperty("key").GetString()).ToArray());
        Assert.All(members, m => Assert.Equal("number", m.GetProperty("style").GetString()));
        Assert.Equal("hp", dnd.GetProperty("enemyCounters").EnumerateArray().Single().GetProperty("key").GetString());

        // Selecting it activates the system but seeds nothing (it has no table counters).
        (await client.PostAsync("/systems/current?id=dnd5e", null)).EnsureSuccessStatusCode();
        var party = await client.GetFromJsonAsync<JsonElement>("/party/list");
        Assert.Equal("dnd5e", party.GetProperty("system").GetString());
        Assert.Empty(party.GetProperty("counters").EnumerateArray());
    }

    // ---- issue #128: the /tv render model carries the system's counter glyphs/colours + spotlight, resolved
    // server-side from the active system, so the key-free TV needs no game knowledge ----

    [Fact]
    public async Task Tv_board_render_resolves_counter_glyphs_and_colors_from_the_active_system()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PostAsync("/systems/current?id=daggerheart", null)).EnsureSuccessStatusCode();

        // A player whose tracks carry the semantic keys under POLISH labels (proving glyphs no longer depend on
        // the label — the pre-#128 bug), plus one custom keyless counter; and a bestiary enemy with hp/stress.
        (await client.PutAsJsonAsync("/party/players/kira", new
        {
            name = "Kira",
            counters = new object[]
            {
                new { label = "Życie", key = "hp", value = 3, max = 6, style = "pips" },
                new { label = "Stres", key = "stress", value = 1, max = 6, style = "pips" },
                new { label = "Pancerz", key = "armor", value = 2, max = 3, style = "pips" },
                new { label = "Nadzieja", key = "hope", value = 4, max = 6, style = "pips" },
                new { label = "Szczęście", value = 1, max = 6, style = "pips" },
            },
        })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/party/enemies/goblin", new
        {
            name = "Goblin",
            counters = new object[]
            {
                new { label = "HP", key = "hp", value = 6, max = 6, style = "pips" },
                new { label = "Stress", key = "stress", value = 0, max = 3, style = "pips" },
            },
        })).EnsureSuccessStatusCode();

        // A board with BOTH live-roster elements (they share the one render model): party left, enemies right.
        (await client.PutAsJsonAsync("/boards/table", new
        {
            name = "Table",
            elements = new object[]
            {
                new { kind = "party", x = 0.0, y = 0.0, w = 40.0, h = 100.0 },
                new { kind = "enemies", x = 60.0, y = 0.0, w = 40.0, h = 100.0 },
            },
        })).EnsureSuccessStatusCode();
        (await client.GetAsync("/tv/show?board=table")).EnsureSuccessStatusCode();

        var els = (await client.GetFromJsonAsync<JsonElement>("/tv/state"))
            .GetProperty("content").GetProperty("board").GetProperty("elements");

        // Party element: the four Daggerheart tracks theme by key regardless of the Polish labels; the custom
        // (keyless) counter stays a neutral dot, and the seeded table Fear is a red dot with no glyph.
        var players = els[0].GetProperty("party").GetProperty("players")[0].GetProperty("counters");
        AssertGlyph(CounterByLabel(players, "Życie"), "heart-broken", "#ff4d5e");
        AssertGlyph(CounterByLabel(players, "Stres"), "heart-dark", null);
        AssertGlyph(CounterByLabel(players, "Pancerz"), "shield-broken", "#cdd4e0");
        AssertGlyph(CounterByLabel(players, "Nadzieja"), "diamond", "#eef2f8");
        AssertGlyph(CounterByLabel(players, "Szczęście"), null, null);
        AssertGlyph(CounterByLabel(els[0].GetProperty("party").GetProperty("counters"), "Fear"), null, "#ff4d2e");

        // Enemies element: enemy tracks resolve against the ENEMY presets (same glyphs for Daggerheart).
        var enemies = els[1].GetProperty("party").GetProperty("enemies")[0].GetProperty("counters");
        AssertGlyph(CounterByLabel(enemies, "HP"), "heart-broken", "#ff4d5e");
        AssertGlyph(CounterByLabel(enemies, "Stress"), "heart-dark", null);
    }

    [Fact]
    public async Task Tv_encounter_render_carries_the_spotlight_label_only_while_a_system_is_active()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        (await client.PutAsJsonAsync("/encounters/fight", new
        {
            name = "Fight",
            enemies = new object[]
            {
                new
                {
                    instanceId = "boss", enemyId = "lich", name = "The Lich", spotlight = true,
                    counters = new object[] { new { label = "HP", key = "hp", value = 20, max = 20, style = "pips" } },
                },
            },
        })).EnsureSuccessStatusCode();
        (await client.GetAsync("/tv/show?encounter=fight")).EnsureSuccessStatusCode();

        // No system: the boss flag is set, but there is no chip text (null hides the chip) and hp stays neutral.
        var noSys = (await client.GetFromJsonAsync<JsonElement>("/tv/state"))
            .GetProperty("content").GetProperty("board").GetProperty("elements")[1]
            .GetProperty("party").GetProperty("enemies")[0];
        Assert.True(noSys.GetProperty("spotlight").GetBoolean());
        Assert.Equal(JsonValueKind.Null, noSys.GetProperty("spotlightLabel").ValueKind);
        AssertGlyph(CounterByLabel(noSys.GetProperty("counters"), "HP"), null, null);

        // Activate Daggerheart: the chip text arrives as the system literal and hp themes (label-independent).
        (await client.PostAsync("/systems/current?id=daggerheart", null)).EnsureSuccessStatusCode();
        var withSys = (await client.GetFromJsonAsync<JsonElement>("/tv/state"))
            .GetProperty("content").GetProperty("board").GetProperty("elements")[1]
            .GetProperty("party").GetProperty("enemies")[0];
        Assert.Equal("SPOTLIGHT", withSys.GetProperty("spotlightLabel").GetString());
        AssertGlyph(CounterByLabel(withSys.GetProperty("counters"), "HP"), "heart-broken", "#ff4d5e");
    }
}
