using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Systems;

namespace AmbientDirector.Api.Endpoints;

public static class TvEndpoints
{
    // The #120 encounter-preset layout coords (percent-of-stage): heroes on the left, enemies on the right. A
    // synthesized encounter view (issue #122) reuses these exact shapes so it renders identically to a hand-built
    // encounter board through the one BoardCanvas renderer. There is deliberately no image/"VS" element — the
    // background carries the art.
    private static readonly (double X, double Y, double W, double H) HeroesRect = (0.5, 2, 31, 96);
    private static readonly (double X, double Y, double W, double H) EnemiesRect = (75.5, 2, 24, 96);
    // The synthesized encounter view also needs a Fear element (issue #144): now that the party element no
    // longer paints the table-counter strip, the skull track is the only place Fear shows. A short, wide strip
    // top-centre, in the gap between the heroes (left) and enemies (right) columns.
    private static readonly (double X, double Y, double W, double H) FearRect = (33, 2, 41, 13);

    public static void MapTvEndpoints(this WebApplication app)
    {
        // The bare "/tv" path is served in Program.cs as a static, framework-free HTML page (wwwroot/tv.html)
        // so the player-facing display boots on old smart-TV browsers that can't run the Blazor WASM panel; the
        // routes below are its data + the GM push commands.
        // Access control: /tv, /tv/state and the read-only /tv/content/* streams stay OUTSIDE the API-key gate
        // so players' shared screens never carry the admin key — the only key-free data is what the GM
        // deliberately pushed (an image, or a board/encounter and the images it references). The push commands
        // (/tv/show*, /tv/clear) ARE gated (see IsProtectedPath in Program.cs).
        var tv = app.MapGroup("/tv");

        // Plain fast poll, mirroring /assistant/state?rev=. The client sends its last-seen rev and always
        // trusts the authoritative rev echoed here (it resets to 1 on a restart, so no monotonic assumption);
        // we keep it dead simple and return the full state every time. `state`/`boards` come first so `rev`
        // can carry a default (required minimal-API parameters must precede optional ones).
        tv.MapGet("/state", async (TvState state, BoardStore boards, PartyStore party, EncounterStore encounters,
            GameSystemRegistry registry, long rev = 0) =>
        {
            var (currentRev, content) = state.Snapshot();
            if (content is null)
                return new TvStateDto(currentRev, null);

            if (content.Kind == "board")
            {
                var board = await boards.GetAsync(content.Ref);
                if (board is null)
                    // Self-healing projection: the shown board was deleted out from under us. Report empty
                    // content WITHOUT mutating state — a GET must stay side-effect-free (a later push/clear or
                    // the delete's own ForgetBoard is what bumps the rev).
                    return new TvStateDto(currentRev, null);

                var boardDto = await BuildBoardDtoAsync(board, party, registry, currentRev);
                // A board carries its render model in Content.Board; Content.Url is null (nothing to stream).
                return new TvStateDto(currentRev, new TvContentDto("board", null, content.Label, boardDto));
            }

            if (content.Kind == "encounter")
            {
                var encounter = await encounters.GetAsync(content.Ref);
                if (encounter is null)
                    return new TvStateDto(currentRev, null); // self-healing, like the board case above

                var encounterDto = await BuildEncounterDtoAsync(encounter, party, registry, currentRev);
                // Reported as kind "board" on the wire so the TV draws it through BoardCanvas UNCHANGED — the
                // synthesized render model IS a board. The internal TvContent.Kind stays "encounter" (for the
                // gate / show / recent); this is the render instruction, not the pushed-content kind.
                return new TvStateDto(currentRev, new TvContentDto("board", null, content.Label, encounterDto));
            }

            // kind "image": Url points at /tv/content/current with the current rev as a cache-buster.
            return new TvStateDto(currentRev,
                new TvContentDto("image", $"/tv/content/current?rev={currentRev}", content.Label));
        });

        // Streams the bytes of the CURRENT image only (never an arbitrary name) — this is the one image the
        // key-free display is allowed to see. 404 when nothing is shown, when a BOARD/ENCOUNTER is shown (their
        // images are served per-name by /tv/content/board/{name} below), or when the file vanished from disk.
        tv.MapGet("/content/current", (TvState state, ImageFileStorage images) =>
        {
            if (state.Current is not { Kind: "image" } content)
                return Results.NotFound();
            var full = images.FullPathForName(content.Ref);
            if (full is null || !File.Exists(full))
                return Results.NotFound();
            return Results.File(full, ImageFileStorage.ContentTypeFor(content.Ref));
        });

        // Streams one image referenced by the CURRENTLY SHOWN board OR encounter. THIS IS THE KEY-FREE GATE
        // INVARIANT: the open TV surface may serve ONLY what the currently-pushed content renders — a board's own
        // referenced files (background + image elements) plus, when it renders the live roster, the current
        // members'/enemies' portraits; or an encounter's background + its heroes' + enemy instances' portraits —
        // never the general /images route to keyless clients. 404 unless the shown content still exists AND
        // `name` is one of the files it renders. ALL of that membership check runs BEFORE any disk access, so
        // this route can never become a file-existence oracle for unreferenced images. Everything else is 404
        // (never 401 — a keyless TV must never need a key; not-found is the safe answer).
        tv.MapGet("/content/board/{name}", async (string name, TvState state, ImageFileStorage images,
            BoardStore boards, PartyStore party, EncounterStore encounters) =>
        {
            var allowed = false;
            if (state.Current is { Kind: "board" } bc)
            {
                var board = await boards.GetAsync(bc.Ref);
                if (board is not null)
                    allowed = board.ReferencedFiles().Contains(name, StringComparer.OrdinalIgnoreCase)
                              || await IsCurrentRosterPortraitAsync(board, name, party);
            }
            else if (state.Current is { Kind: "encounter" } ec)
            {
                var encounter = await encounters.GetAsync(ec.Ref);
                if (encounter is not null)
                    allowed = await IsEncounterFileAsync(encounter, name, party);
            }
            if (!allowed)
                return Results.NotFound();

            // Only now touch the filesystem — FullPathForName also traversal-guards the name.
            var full = images.FullPathForName(name);
            if (full is null || !File.Exists(full))
                return Results.NotFound();
            return Results.File(full, ImageFileStorage.ContentTypeFor(name));
        });

        // Push a prepared image/handout, a saved board, OR a saved encounter to the display. GET+POST so a
        // Stream Deck "System → Website" button works. Exactly one of ?image= / ?board= / ?encounter= must be given.
        tv.MapMethods("/show", EndpointHelpers.GetOrPost,
            async (string? image, string? board, string? encounter, string? label, TvState state,
                ImageFileStorage images, BoardStore boards, EncounterStore encounters) =>
        {
            var imageName = image?.Trim();
            var boardId = board?.Trim();
            var encounterId = encounter?.Trim();
            var targets = (string.IsNullOrEmpty(imageName) ? 0 : 1)
                          + (string.IsNullOrEmpty(boardId) ? 0 : 1)
                          + (string.IsNullOrEmpty(encounterId) ? 0 : 1);
            if (targets != 1) // none, or more than one
                throw new ValidationException("error.tv.showTarget");

            var trimmedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();

            if (!string.IsNullOrEmpty(imageName))
            {
                // The name is validated inside FullPathForName (traversal-guarded) and must exist on disk.
                var full = images.FullPathForName(imageName);
                if (full is null || !File.Exists(full))
                    throw new ValidationException("error.tv.imageNotFound", imageName);
                var rev = state.Show("image", imageName, trimmedLabel);
                return Results.Ok(new { rev, image = imageName });
            }

            if (!string.IsNullOrEmpty(boardId))
            {
                var b = await boards.GetAsync(boardId)
                    ?? throw new ValidationException("error.tv.boardNotFound", boardId);
                // Default the display label to the board's own name; store the canonical DB-cased id.
                var boardRev = state.Show("board", b.Id, trimmedLabel ?? b.Name);
                return Results.Ok(new { rev = boardRev, board = b.Id });
            }

            var enc = await encounters.GetAsync(encounterId!)
                ?? throw new ValidationException("error.tv.encounterNotFound", encounterId!);
            var encRev = state.ShowEncounter(enc.Id, trimmedLabel ?? enc.Name);
            return Results.Ok(new { rev = encRev, encounter = enc.Id });
        });

        // Clear the display. GET+POST, like /show.
        tv.MapMethods("/clear", EndpointHelpers.GetOrPost, (TvState state) =>
            Results.Ok(new { rev = state.Clear(), cleared = true }));

        // Recently pushed content for the panel's re-push tiles. PROTECTED automatically: the path starts with
        // "/tv/show", which IsProtectedPath gates — folding it under /show keeps history off the open surface
        // without adding another prefix rule. Panel-only, so it exposes the raw stored ref (file name / id).
        tv.MapGet("/show/recent", (TvState state) =>
            state.Recent.Select(c => new TvRecentItemDto(c.Kind, c.Ref, c.Label, c.PushedAtUtc)));
    }

    // ---- render-model builders (shared helpers) ----

    // Resolve a stored image file name to the gate-validated per-name board route (with the rev cache-buster),
    // or null. Shared by the board + encounter projections — both stream through /tv/content/board/{name}.
    private static string? BoardImageUrl(string? name, long rev) =>
        string.IsNullOrEmpty(name) ? null : $"/tv/content/board/{Uri.EscapeDataString(name)}?rev={rev}";

    // Resolve one counter's render presentation (issue #128): its semantic Key → the matching preset in the
    // given scope (member/enemy/table) of the active system → the preset's curated Glyph + content Color. No
    // presets (no active system), no key, or no match → both null (a neutral dot). This is the whole of the
    // "server resolves presentation" step — BoardCanvas then draws straight from Glyph/Color.
    private static TvPartyCounterDto Counter(PartyCounter c, IReadOnlyList<CounterPreset>? presets)
    {
        var preset = presets is null || string.IsNullOrEmpty(c.Key)
            ? null
            : presets.FirstOrDefault(p => string.Equals(p.Key, c.Key, StringComparison.OrdinalIgnoreCase));
        // The counter's own Key rides along too (issue #144) so the fear board element can find the fear-keyed
        // table counter client-side — its localized label can't be matched.
        return new TvPartyCounterDto(c.Label, c.Value, c.Max, c.Style, preset?.Glyph, preset?.Color, c.Key);
    }

    private static TvEnemyDto EnemyDto(string name, string? portrait, bool spotlight, List<PartyCounter> counters,
        IGameSystem? system, long rev) =>
        new(name, BoardImageUrl(portrait, rev), spotlight,
            [.. counters.Select(c => Counter(c, system?.EnemyCounters))], system?.SpotlightLabel);

    private static TvPartyPlayerDto PlayerDto(PartyMember m, IGameSystem? system, long rev) =>
        new(m.Name, BoardImageUrl(m.Portrait, rev), [.. m.Counters.Select(c => Counter(c, system?.MemberCounters))]);

    // A saved board's render model. A kind=party / kind=enemies / kind=fear element renders the LIVE roster (not
    // board state). Load it ONCE and attach the SAME render model instance to every such element (there is
    // normally one of each); a board with none of the three skips the query entirely. The party element reads
    // Players, the enemies element reads Enemies (the bestiary templates — base stats, no per-instance
    // spotlight), and the fear element reads Counters (its fear-keyed table counter — issue #144).
    private static async Task<TvBoardDto> BuildBoardDtoAsync(Board board, PartyStore party,
        GameSystemRegistry registry, long rev)
    {
        TvPartyDto? partyDto = null;
        if (board.Elements.Any(e => e.Kind is "party" or "enemies" or "fear"))
        {
            // The active system (issue #128) resolves each counter's glyph/colour; loaded once, only when a
            // live-roster element actually needs it (an image-only board skips this query, like the roster one).
            var system = registry.Find(await party.GetSystemIdAsync());
            var members = await party.GetMembersAsync();
            var tableCounters = await party.GetTableCountersAsync();
            var enemies = await party.GetEnemiesAsync();
            partyDto = new TvPartyDto(
                [.. members.Select(m => PlayerDto(m, system, rev))],
                [.. tableCounters.Select(c => Counter(c, system?.TableCounters))],
                [.. enemies.Select(en => EnemyDto(en.Name, en.Portrait, false, en.Counters, system, rev))]);
        }

        return new TvBoardDto(
            board.BackgroundColor,
            BoardImageUrl(board.BackgroundImage, rev),
            [.. board.Elements.Select(e => new TvBoardElementDto(
                e.Kind, e.X, e.Y, e.W, e.H,
                // Image refs resolve to the gate-validated per-name board route; text fields pass through.
                e.Kind == "image" && !string.IsNullOrEmpty(e.Image) ? BoardImageUrl(e.Image, rev) : null,
                e.Kind == "text" ? e.Text : null,
                e.Kind == "text" ? e.Color : null,
                e.Kind == "text" ? e.Size : null,
                e.Kind == "text" ? e.Align : null,
                e.Kind is "party" or "enemies" or "fear" ? partyDto : null))]);
    }

    // Synthesize an encounter into a board render model (issue #122): background image, a party element on the
    // left carrying the chosen heroes (resolved from HeroIds against the live party; empty ⇒ all players), an
    // enemies element on the right carrying the encounter's own instances, and a fear element top-centre for the
    // table's Fear (issue #144 — the party element no longer paints the table-counter strip, so this is where
    // Fear shows). All three elements share the SAME render model instance (BoardCanvas reads Players for
    // "party", Enemies for "enemies", the fear-keyed Counter for "fear"), so their coords come straight from the
    // #120/#144 presets — no BoardCanvas change.
    private static async Task<TvBoardDto> BuildEncounterDtoAsync(Encounter encounter, PartyStore party,
        GameSystemRegistry registry, long rev)
    {
        var system = registry.Find(await party.GetSystemIdAsync());
        var members = await party.GetMembersAsync();
        var tableCounters = await party.GetTableCountersAsync();
        var heroes = ResolveHeroes(encounter, members);

        var partyDto = new TvPartyDto(
            [.. heroes.Select(m => PlayerDto(m, system, rev))],
            [.. tableCounters.Select(c => Counter(c, system?.TableCounters))],
            // Held-back instances are skipped on the TV but kept in the encounter (issue #122 follow-up).
            [.. encounter.Enemies.Where(en => !en.Hidden)
                .Select(en => EnemyDto(en.Name, en.Portrait, en.Spotlight, en.Counters, system, rev))]);

        List<TvBoardElementDto> elements =
        [
            new("party", HeroesRect.X, HeroesRect.Y, HeroesRect.W, HeroesRect.H,
                null, null, null, null, null, partyDto),
            new("enemies", EnemiesRect.X, EnemiesRect.Y, EnemiesRect.W, EnemiesRect.H,
                null, null, null, null, null, partyDto),
            new("fear", FearRect.X, FearRect.Y, FearRect.W, FearRect.H,
                null, null, null, null, null, partyDto),
        ];
        // No BackgroundColor — the encounter art (or the renderer default black) fills the stage.
        return new TvBoardDto(null, BoardImageUrl(encounter.BackgroundImage, rev), elements);
    }

    // The encounter's chosen heroes as live party members, in the encounter's HeroIds order; an empty HeroIds
    // means "all current players". A since-deleted id just drops out (Where filters the misses).
    private static List<PartyMember> ResolveHeroes(Encounter encounter, List<PartyMember> members)
    {
        if (encounter.HeroIds.Count == 0)
            return members;
        return [.. encounter.HeroIds
            .Select(id => members.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(m => m is not null)
            .Select(m => m!)];
    }

    // ---- the dynamic half of the key-free gate (all run BEFORE any filesystem access) ----

    // True when the shown board renders a LIVE ROSTER AND `name` is a current member's OR bestiary enemy's
    // portrait. Portraits are LIVE data, not board files (deliberately absent from Board.ReferencedFiles()), so
    // they're gated here — served key-free only while a live-roster board (a party OR enemies element) is shown.
    // Deliberately NOT widened to "fear" (issue #144): a fear element draws only the static skull art and serves
    // no portraits, so the key-free gate must not open portraits on its account.
    private static async Task<bool> IsCurrentRosterPortraitAsync(Board board, string name, PartyStore party)
    {
        if (!board.Elements.Any(e => e.Kind is "party" or "enemies"))
            return false;
        var members = await party.GetMembersAsync();
        if (members.Any(m => Matches(m.Portrait, name)))
            return true;
        var enemies = await party.GetEnemiesAsync();
        return enemies.Any(e => Matches(e.Portrait, name));
    }

    // True when `name` is one of the files the currently-shown encounter renders: its background, one of its
    // heroes' portraits (live party data), or one of its enemy instances' portraits (snapshots on the instance).
    // The encounter enumerates them itself (Encounter.PortraitFiles), given the live hero portraits.
    private static async Task<bool> IsEncounterFileAsync(Encounter encounter, string name, PartyStore party)
    {
        var members = await party.GetMembersAsync();
        var heroPortraits = ResolveHeroes(encounter, members)
            .Select(m => m.Portrait)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!);
        return encounter.PortraitFiles(heroPortraits).Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool Matches(string? stored, string name) =>
        !string.IsNullOrEmpty(stored) && string.Equals(stored, name, StringComparison.OrdinalIgnoreCase);
}
