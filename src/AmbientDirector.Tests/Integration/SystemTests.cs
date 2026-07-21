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
}
