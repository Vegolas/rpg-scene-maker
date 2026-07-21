using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services.Ai;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// The party / bestiary / encounter / board / TV AI ops added on the shared <see cref="AiToolService"/> façade
/// (the MCP + assistant surface, issue #89 following #120/#122). These resolve the real singleton façade from
/// the booted app and exercise it directly — proving it reuses the same stores, validators, clamping and
/// <c>TvState</c> side effects as the HTTP endpoints (the "reuses the exact HTTP paths' machinery" invariant),
/// not just that the tool NAMES line up (AiToolSurfaceParityTests covers that).
/// </summary>
[Collection("integration")]
public class AiToolServicePartyEncounterTests
{
    private static AiToolService Facade(ApiFactory factory) =>
        factory.Services.GetRequiredService<AiToolService>();

    private static PartyCounter Counter(string label, int value, int? max = null, string? style = null) =>
        new() { Label = label, Value = value, Max = max, Style = style };

    // ---- 1. Party + bestiary CRUD round-trips through list_party ----

    [Fact]
    public async Task List_party_reflects_player_enemy_and_table_counter_upserts()
    {
        using var factory = new ApiFactory();
        var ai = Facade(factory);

        await ai.UpsertPlayerAsync(new PartyMember
        {
            Name = "Kira",
            Counters = [Counter("HP", 7, 10, "number")],
        }, "kira");
        await ai.UpsertEnemyAsync(new Enemy
        {
            Name = "Goblin",
            Counters = [Counter("HP", 0, 8)],
        }, "goblin");
        await ai.SaveTableCountersAsync([Counter("Fear", 3, 12)]);

        var party = await ai.ListPartyAsync();
        Assert.Equal("kira", Assert.Single(party.Players).Id);
        Assert.Equal("goblin", Assert.Single(party.Enemies).Id);
        Assert.Equal("Fear", Assert.Single(party.Counters).Label);

        // delete_player round-trips too (true then false).
        Assert.True(await ai.DeletePlayerAsync("kira"));
        Assert.False(await ai.DeletePlayerAsync("kira"));
    }

    // ---- 2. adjust ops: the endpoints' XOR guard + clamping + not-found, verbatim ----

    [Fact]
    public async Task Adjust_player_counter_clamps_and_enforces_the_xor_and_not_found_guards()
    {
        using var factory = new ApiFactory();
        var ai = Facade(factory);
        await ai.UpsertPlayerAsync(new PartyMember { Name = "Kira", Counters = [Counter("HP", 7, 10)] }, "kira");

        // delta bumps then clamps at 0; value sets absolutely then clamps to Max.
        Assert.Equal(0, HpOf(await ai.AdjustPlayerCounterAsync("kira", "HP", delta: -99, value: null)));
        Assert.Equal(10, HpOf(await ai.AdjustPlayerCounterAsync("kira", "HP", delta: null, value: 99)));

        // XOR: neither and both are ValidationException (an ArgumentException — both AI surfaces catch it).
        await Assert.ThrowsAsync<ValidationException>(() => ai.AdjustPlayerCounterAsync("kira", "HP", null, null));
        await Assert.ThrowsAsync<ValidationException>(() => ai.AdjustPlayerCounterAsync("kira", "HP", 1, 1));
        // A blank counter label is a ValidationException; an unknown player/counter is a NotFoundException.
        await Assert.ThrowsAsync<ValidationException>(() => ai.AdjustPlayerCounterAsync("kira", " ", 1, null));
        await Assert.ThrowsAsync<NotFoundException>(() => ai.AdjustPlayerCounterAsync("ghost", "HP", 1, null));
        await Assert.ThrowsAsync<NotFoundException>(() => ai.AdjustPlayerCounterAsync("kira", "Mana", 1, null));

        static int HpOf(PartyMember m) => m.Counters.Single(c => c.Label == "HP").Value;
    }

    // ---- 3. Encounters: run best-effort + live instance tracking + reset ----

    [Fact]
    public async Task Run_encounter_shows_on_the_tv_and_reports_best_effort_statuses()
    {
        using var factory = new ApiFactory();
        var ai = Facade(factory);

        await ai.UpsertEncounterAsync(new Encounter
        {
            Name = "Goblin Ambush",
            Enemies =
            [
                new EncounterEnemy { InstanceId = "g1", EnemyId = "goblin", Name = "Goblin 1", Counters = [Counter("HP", 6, 6)] },
            ],
        }, "goblins");

        // No scene/event configured → both "skipped"; the encounter still lands on the TV.
        var run = JsonSerializer.SerializeToElement(await ai.RunEncounterAsync("goblins"), AiJson.Options);
        Assert.Equal("skipped", run.GetProperty("scene").GetString());
        Assert.Equal("skipped", run.GetProperty("event").GetString());

        var tv = ai.GetTvState();
        Assert.Equal("encounter", tv.Kind);
        Assert.Equal("goblins", tv.Ref);

        // Unknown encounter → NotFoundException (mirrors the endpoint's 404).
        await Assert.ThrowsAsync<NotFoundException>(() => ai.RunEncounterAsync("nope"));
    }

    [Fact]
    public async Task Adjust_and_reset_encounter_enemy_track_the_live_instance()
    {
        using var factory = new ApiFactory();
        var ai = Facade(factory);
        await ai.UpsertEnemyAsync(new Enemy { Name = "Goblin", Counters = [Counter("HP", 0, 8)] }, "goblin");
        await ai.UpsertEncounterAsync(new Encounter
        {
            Name = "Fight",
            Enemies = [new EncounterEnemy { InstanceId = "g1", EnemyId = "goblin", Name = "Goblin 1", Counters = [Counter("HP", 0, 8)] }],
        }, "fight");

        var marked = await ai.AdjustEncounterEnemyAsync("fight", "g1", "HP", delta: null, value: 5);
        Assert.Equal(5, marked.Enemies[0].Counters.Single(c => c.Label == "HP").Value);

        // reset re-seeds from the statblock's starting value (0), not to Max.
        var reset = await ai.ResetEncounterAsync("fight");
        Assert.Equal(0, reset.Enemies[0].Counters.Single(c => c.Label == "HP").Value);

        await Assert.ThrowsAsync<NotFoundException>(() => ai.AdjustEncounterEnemyAsync("fight", "ghost", "HP", 1, null));
    }

    // ---- 4. TV: show XOR + clear, and the live-roster rev bump the endpoints emit ----

    [Fact]
    public async Task Show_on_tv_enforces_exactly_one_target_and_clears()
    {
        using var factory = new ApiFactory();
        var ai = Facade(factory);
        await ai.UpsertBoardAsync(new Board { Name = "Plain" }, "plain");
        await ai.UpsertEncounterAsync(new Encounter { Name = "Fight" }, "fight");

        // None / more than one target → ValidationException (the /tv/show XOR).
        await Assert.ThrowsAsync<ValidationException>(() => ai.ShowOnTvAsync(null, null, null, null));
        await Assert.ThrowsAsync<ValidationException>(() => ai.ShowOnTvAsync(null, "plain", "fight", null));
        // An unknown board target → ValidationException (boardNotFound).
        await Assert.ThrowsAsync<ValidationException>(() => ai.ShowOnTvAsync(null, "ghost", null, null));

        await ai.ShowOnTvAsync(null, "plain", null, null);
        Assert.Equal("board", ai.GetTvState().Kind);

        ai.ClearTv();
        Assert.Null(ai.GetTvState().Kind);
    }

    [Fact]
    public async Task Party_changes_bump_the_tv_rev_only_while_a_live_roster_is_shown()
    {
        using var factory = new ApiFactory();
        var ai = Facade(factory);
        await ai.UpsertPlayerAsync(new PartyMember { Name = "Kira", Counters = [Counter("HP", 5, 10)] }, "kira");
        // A board carrying a live party element (geometry only — it renders the roster at display time).
        await ai.UpsertBoardAsync(new Board
        {
            Name = "Roster",
            Elements = [new BoardElement { Kind = "party", X = 0, Y = 0, W = 30, H = 90 }],
        }, "roster");

        // Nothing shown yet → an adjust does not bump.
        var before = ai.GetTvState().Rev;
        await ai.AdjustPlayerCounterAsync("kira", "HP", delta: -1, value: null);
        Assert.Equal(before, ai.GetTvState().Rev);

        // Show the roster board, then a player adjust bumps the rev (TouchIfPartyShown), so an open TV re-fetches.
        await ai.ShowOnTvAsync(null, "roster", null, null);
        var shown = ai.GetTvState().Rev;
        await ai.AdjustPlayerCounterAsync("kira", "HP", delta: -1, value: null);
        Assert.True(ai.GetTvState().Rev > shown, "a player change while a party board is shown should bump the rev");
    }
}
