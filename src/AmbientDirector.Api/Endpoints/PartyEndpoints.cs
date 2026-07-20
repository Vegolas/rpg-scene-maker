using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Endpoints;

public static class PartyEndpoints
{
    public static void MapPartyEndpoints(this WebApplication app)
    {
        // The party tracker (issue #88, Phase 3): the roster of players and their live counters, rendered on
        // the key-free /tv display by a board's kind="party" element. Purely data + a couple of tap-to-adjust
        // commands — there is no /trigger.
        var party = app.MapGroup("/party");

        // Literal segment, so it wins over any "/{id}" route.
        party.MapGet("/list", async (PartyStore store) =>
            new PartyDto(await store.GetMembersAsync(), await store.GetTableCountersAsync()));

        party.MapPut("/players/{id}", async (string id, PartyMember member, PartyStore store,
            ImageFileStorage images, TvState tvState, BoardStore boards) =>
        {
            member.Id = id;
            PartyValidation.Validate(member);
            // Own the member's portrait and clean up on replace: capture the old file before the upsert and drop
            // it afterwards if it changed (the Screen/Scene single-image ownership pattern).
            var oldPortrait = (await store.GetMemberAsync(id))?.Portrait;
            await store.UpsertMemberAsync(member);
            if (!string.IsNullOrEmpty(oldPortrait) &&
                !string.Equals(oldPortrait, member.Portrait, StringComparison.OrdinalIgnoreCase))
                images.Delete(oldPortrait);
            await TouchIfPartyShownAsync(tvState, boards);
            return Results.Ok(member);
        });

        party.MapDelete("/players/{id}", async (string id, PartyStore store,
            ImageFileStorage images, TvState tvState, BoardStore boards) =>
        {
            // Capture the portrait before deleting so we can release the member's upload afterwards.
            var portrait = (await store.GetMemberAsync(id))?.Portrait;
            if (!await store.DeleteMemberAsync(id)) return Results.NotFound();
            images.Delete(portrait);
            await TouchIfPartyShownAsync(tvState, boards);
            return Results.NoContent();
        });

        party.MapPut("/counters", async (List<PartyCounter> counters, PartyStore store,
            TvState tvState, BoardStore boards) =>
        {
            counters ??= [];
            PartyValidation.ValidateCounters(counters); // normalizes (trims labels, clamps values) in place
            await store.SaveTableCountersAsync(counters);
            await TouchIfPartyShownAsync(tvState, boards);
            return Results.Ok(counters);
        });

        // Tap-to-adjust a member's counter. GET+POST so a Stream Deck button can do
        // /party/players/kira/adjust?counter=HP&delta=-1. Exactly one of ?delta= / ?value= must be given
        // (mirrors /tv/show's XOR): delta bumps the current value, value sets it absolutely.
        party.MapMethods("/players/{id}/adjust", EndpointHelpers.GetOrPost,
            async (string id, string? counter, int? delta, int? value, PartyStore store,
                TvState tvState, BoardStore boards) =>
        {
            if (string.IsNullOrWhiteSpace(counter))
                throw new ValidationException("error.party.adjustCounter");
            if (delta.HasValue == value.HasValue) // both, or neither
                throw new ValidationException("error.party.adjustTarget");
            var updated = await store.AdjustMemberCounterAsync(id, counter.Trim(), delta, value);
            await TouchIfPartyShownAsync(tvState, boards);
            return Results.Ok(updated);
        });

        // Tap-to-adjust a table-level counter (Fear etc.). Same shape as the per-member adjust above.
        party.MapMethods("/counters/adjust", EndpointHelpers.GetOrPost,
            async (string? counter, int? delta, int? value, PartyStore store,
                TvState tvState, BoardStore boards) =>
        {
            if (string.IsNullOrWhiteSpace(counter))
                throw new ValidationException("error.party.adjustCounter");
            if (delta.HasValue == value.HasValue) // both, or neither
                throw new ValidationException("error.party.adjustTarget");
            var updated = await store.AdjustTableCounterAsync(counter.Trim(), delta, value);
            await TouchIfPartyShownAsync(tvState, boards);
            return Results.Ok(updated);
        });

        // Deliberately NO "GET /party/players/{id}" (and nothing at the bare "/party"): the Blazor panel's
        // party page lives at /party and each member editor at /party/{id}, so a full-page load of either must
        // fall through to index.html. The panel reads the whole roster from /party/list and picks the member by
        // id client-side. (MapFallbackToFile only serves GET, so the PUT/DELETE above are safe.)
    }

    // Any party change is instantly visible on the TV IF the currently-shown board renders the party:
    // bump the rev (via TouchBoard) so the open display re-fetches within one 2 s poll. A shown image or a
    // board without a party element is untouched — no pointless re-renders.
    static async Task TouchIfPartyShownAsync(TvState tv, BoardStore boards)
    {
        if (tv.Current is not { Kind: "board" } current) return;
        var board = await boards.GetAsync(current.Ref);
        if (board is not null && board.Elements.Any(e => e.Kind == "party"))
            tv.TouchBoard(board.Id);
    }
}
