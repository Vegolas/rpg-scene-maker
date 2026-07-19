using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services;

namespace AmbientDirector.Api.Endpoints;

public static class TvEndpoints
{
    public static void MapTvEndpoints(this WebApplication app)
    {
        // Nothing is mapped at the bare "/tv" path: the Blazor panel's full-screen player display lives there,
        // so a full-page load of /tv must fall through to index.html (same trick as /sounds and /screens).
        // Access control: /tv, /tv/state and /tv/content/current stay OUTSIDE the API-key gate so players'
        // shared screens never carry the admin key — the only key-free data is the image the GM deliberately
        // pushed. The push commands (/tv/show*, /tv/clear) ARE gated (see IsProtectedPath in Program.cs).
        var tv = app.MapGroup("/tv");

        // Plain fast poll, mirroring /assistant/state?rev=. The client sends its last-seen rev and always
        // trusts the authoritative rev echoed here (it resets to 1 on a restart, so no monotonic assumption);
        // we keep it dead simple and return the full state every time. `state` comes first so `rev` can carry
        // a default (required minimal-API parameters must precede optional ones).
        tv.MapGet("/state", (TvState state, long rev = 0) =>
        {
            var (currentRev, content) = state.Snapshot();
            return new TvStateDto(currentRev, content is null
                ? null
                : new TvContentDto(content.Kind, $"/tv/content/current?rev={currentRev}", content.Label));
        });

        // Streams the bytes of the CURRENT image only (never an arbitrary name) — this is the one image the
        // key-free display is allowed to see. 404 when nothing is shown or the file vanished from disk.
        tv.MapGet("/content/current", (TvState state, ImageFileStorage images) =>
        {
            if (state.Current is not { } content)
                return Results.NotFound();
            var full = images.FullPathForName(content.File);
            if (full is null || !File.Exists(full))
                return Results.NotFound();
            return Results.File(full, ImageFileStorage.ContentTypeFor(content.File));
        });

        // Push a prepared image/handout to the display. GET+POST so a Stream Deck "System → Website" button
        // works. The name is validated inside FullPathForName (traversal-guarded) and must exist on disk.
        tv.MapMethods("/show", EndpointHelpers.GetOrPost,
            (string? image, string? label, TvState state, ImageFileStorage images) =>
        {
            var name = image?.Trim();
            var full = string.IsNullOrEmpty(name) ? null : images.FullPathForName(name);
            if (name is null || full is null || !File.Exists(full))
                throw new ValidationException("error.tv.imageNotFound", name ?? "");
            var rev = state.Show(name, string.IsNullOrWhiteSpace(label) ? null : label.Trim());
            return Results.Ok(new { rev, image = name });
        });

        // Clear the display. GET+POST, like /show.
        tv.MapMethods("/clear", EndpointHelpers.GetOrPost, (TvState state) =>
            Results.Ok(new { rev = state.Clear(), cleared = true }));

        // Recently pushed content for the panel's re-push tiles. PROTECTED automatically: the path starts with
        // "/tv/show", which IsProtectedPath gates — folding it under /show keeps history off the open surface
        // without adding another prefix rule. Panel-only, so it exposes the raw stored file names.
        tv.MapGet("/show/recent", (TvState state) =>
            state.Recent.Select(c => new TvRecentItemDto(c.Kind, c.File, c.Label, c.PushedAtUtc)));
    }
}
