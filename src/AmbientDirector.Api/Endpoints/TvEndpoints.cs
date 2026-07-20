using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;

namespace AmbientDirector.Api.Endpoints;

public static class TvEndpoints
{
    public static void MapTvEndpoints(this WebApplication app)
    {
        // Nothing is mapped at the bare "/tv" path: the Blazor panel's full-screen player display lives there,
        // so a full-page load of /tv must fall through to index.html (same trick as /sounds and /screens).
        // Access control: /tv, /tv/state and the read-only /tv/content/* streams stay OUTSIDE the API-key gate
        // so players' shared screens never carry the admin key — the only key-free data is what the GM
        // deliberately pushed (an image, or a board and the images that board references). The push commands
        // (/tv/show*, /tv/clear) ARE gated (see IsProtectedPath in Program.cs).
        var tv = app.MapGroup("/tv");

        // Plain fast poll, mirroring /assistant/state?rev=. The client sends its last-seen rev and always
        // trusts the authoritative rev echoed here (it resets to 1 on a restart, so no monotonic assumption);
        // we keep it dead simple and return the full state every time. `state`/`boards` come first so `rev`
        // can carry a default (required minimal-API parameters must precede optional ones).
        tv.MapGet("/state", async (TvState state, BoardStore boards, PartyStore party, long rev = 0) =>
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

                // A kind=party / kind=enemies element renders the LIVE roster (not board state). Load it ONCE
                // here and attach the SAME render model instance to every such element on the board (there is
                // normally one of each); a board with neither skips the query entirely. Both kinds share one
                // query and one instance — the party element reads Players/Counters, the enemies element reads
                // Enemies. Portrait refs resolve to the same gate-validated per-name board route as image
                // elements (the gate allows them dynamically).
                TvPartyDto? partyDto = null;
                if (board.Elements.Any(e => e.Kind is "party" or "enemies"))
                {
                    var members = await party.GetMembersAsync();
                    var tableCounters = await party.GetTableCountersAsync();
                    var enemies = await party.GetEnemiesAsync();
                    partyDto = new TvPartyDto(
                        [.. members.Select(m => new TvPartyPlayerDto(
                            m.Name,
                            string.IsNullOrEmpty(m.Portrait)
                                ? null
                                : $"/tv/content/board/{Uri.EscapeDataString(m.Portrait)}?rev={currentRev}",
                            [.. m.Counters.Select(c => new TvPartyCounterDto(c.Label, c.Value, c.Max, c.Style))]))],
                        [.. tableCounters.Select(c => new TvPartyCounterDto(c.Label, c.Value, c.Max, c.Style))],
                        [.. enemies.Select(en => new TvEnemyDto(
                            en.Name,
                            en.Spotlight,
                            [.. en.Counters.Select(c => new TvPartyCounterDto(c.Label, c.Value, c.Max, c.Style))]))]);
                }

                var boardDto = new TvBoardDto(
                    board.BackgroundColor,
                    string.IsNullOrEmpty(board.BackgroundImage)
                        ? null
                        : $"/tv/content/board/{Uri.EscapeDataString(board.BackgroundImage)}?rev={currentRev}",
                    [.. board.Elements.Select(e => new TvBoardElementDto(
                        e.Kind, e.X, e.Y, e.W, e.H,
                        // Image refs resolve to the gate-validated per-name board route; text fields pass through.
                        e.Kind == "image" && !string.IsNullOrEmpty(e.Image)
                            ? $"/tv/content/board/{Uri.EscapeDataString(e.Image)}?rev={currentRev}"
                            : null,
                        e.Kind == "text" ? e.Text : null,
                        e.Kind == "text" ? e.Color : null,
                        e.Kind == "text" ? e.Size : null,
                        e.Kind == "text" ? e.Align : null,
                        // A party/enemies element carries the same live-roster instance; every other kind
                        // leaves it null. The renderer reads Players/Counters for "party", Enemies for "enemies".
                        e.Kind is "party" or "enemies" ? partyDto : null))]);

                // A board carries its render model in Content.Board; Content.Url is null (nothing to stream).
                return new TvStateDto(currentRev, new TvContentDto("board", null, content.Label, boardDto));
            }

            // kind "image": Url points at /tv/content/current with the current rev as a cache-buster.
            return new TvStateDto(currentRev,
                new TvContentDto("image", $"/tv/content/current?rev={currentRev}", content.Label));
        });

        // Streams the bytes of the CURRENT image only (never an arbitrary name) — this is the one image the
        // key-free display is allowed to see. 404 when nothing is shown, when a BOARD is shown (a board's
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

        // Streams one image referenced by the CURRENTLY SHOWN board. THIS IS THE KEY-FREE GATE INVARIANT: the
        // open TV surface may serve ONLY what the currently-pushed board renders — its own referenced files
        // (background + image elements), PLUS, when the board renders the live party, the portraits of the
        // current members — never the general /images route to keyless clients. 404 unless (a) a board is
        // currently shown, (b) that board still exists, and (c) `name` is one of its ReferencedFiles() OR a
        // current member's portrait on a party-element board. ALL of that membership check runs BEFORE any disk
        // access, so this route can never become a file-existence oracle for unreferenced images. Everything
        // else is 404 (never 401 — a keyless TV must never need a key; not-found is the safe answer).
        tv.MapGet("/content/board/{name}", async (string name, TvState state, ImageFileStorage images, BoardStore boards, PartyStore party) =>
        {
            if (state.Current is not { Kind: "board" } content)
                return Results.NotFound();
            var board = await boards.GetAsync(content.Ref);
            if (board is null)
                return Results.NotFound();
            if (!board.ReferencedFiles().Contains(name, StringComparer.OrdinalIgnoreCase)
                && !await IsCurrentPartyPortraitAsync(board, name, party))
                return Results.NotFound();
            // Only now touch the filesystem — FullPathForName also traversal-guards the name.
            var full = images.FullPathForName(name);
            if (full is null || !File.Exists(full))
                return Results.NotFound();
            return Results.File(full, ImageFileStorage.ContentTypeFor(name));
        });

        // Push a prepared image/handout OR a saved board to the display. GET+POST so a Stream Deck
        // "System → Website" button works. Exactly one of ?image= / ?board= must be given.
        tv.MapMethods("/show", EndpointHelpers.GetOrPost,
            async (string? image, string? board, string? label, TvState state, ImageFileStorage images, BoardStore boards) =>
        {
            var imageName = image?.Trim();
            var boardId = board?.Trim();
            var hasImage = !string.IsNullOrEmpty(imageName);
            var hasBoard = !string.IsNullOrEmpty(boardId);
            if (hasImage == hasBoard) // both, or neither
                throw new ValidationException("error.tv.showTarget");

            var trimmedLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();

            if (hasImage)
            {
                // The name is validated inside FullPathForName (traversal-guarded) and must exist on disk.
                var full = images.FullPathForName(imageName!);
                if (full is null || !File.Exists(full))
                    throw new ValidationException("error.tv.imageNotFound", imageName!);
                var rev = state.Show("image", imageName!, trimmedLabel);
                return Results.Ok(new { rev, image = imageName });
            }

            var b = await boards.GetAsync(boardId!)
                ?? throw new ValidationException("error.tv.boardNotFound", boardId!);
            // Default the display label to the board's own name; store the canonical DB-cased id.
            var boardRev = state.Show("board", b.Id, trimmedLabel ?? b.Name);
            return Results.Ok(new { rev = boardRev, board = b.Id });
        });

        // Clear the display. GET+POST, like /show.
        tv.MapMethods("/clear", EndpointHelpers.GetOrPost, (TvState state) =>
            Results.Ok(new { rev = state.Clear(), cleared = true }));

        // Recently pushed content for the panel's re-push tiles. PROTECTED automatically: the path starts with
        // "/tv/show", which IsProtectedPath gates — folding it under /show keeps history off the open surface
        // without adding another prefix rule. Panel-only, so it exposes the raw stored ref (file name / board id).
        tv.MapGet("/show/recent", (TvState state) =>
            state.Recent.Select(c => new TvRecentItemDto(c.Kind, c.Ref, c.Label, c.PushedAtUtc)));
    }

    // The dynamic half of the key-free gate: true when the shown board renders a LIVE ROSTER AND `name` is a
    // current member's portrait. Portraits are LIVE data, not board files (they're deliberately absent from
    // Board.ReferencedFiles()), so they're gated here instead — served key-free only while a live-roster board
    // (a party OR enemies element) is on the display. Party and enemies are treated the same everywhere: the
    // gate is about which board is shown, not which element draws the portrait, and on an encounter board the
    // two coexist. Runs entirely BEFORE any filesystem access, like the ReferencedFiles() check.
    static async Task<bool> IsCurrentPartyPortraitAsync(Board board, string name, PartyStore party)
    {
        if (!board.Elements.Any(e => e.Kind is "party" or "enemies"))
            return false;
        var members = await party.GetMembersAsync();
        return members.Any(m => !string.IsNullOrEmpty(m.Portrait)
            && string.Equals(m.Portrait, name, StringComparison.OrdinalIgnoreCase));
    }
}
