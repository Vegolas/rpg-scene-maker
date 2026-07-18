using Microsoft.AspNetCore.Http.Features;
using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Services.Music;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Endpoints;

public static class MusicEndpoints
{
    // Cap uploads like the soundboard so a stray huge file can't fill the disk; generous for long tracks.
    private const long MaxUploadBytes = 50 * 1024 * 1024;

    public static void MapMusicEndpoints(this WebApplication app)
    {
        // Nothing is mapped at the bare "/music" path: the panel's Music tab lives there, so a full-page load
        // must fall through to index.html (same reason /sounds and /lights use /…/list).
        var music = app.MapGroup("/music");

        // ---------- transport (source-aware via the router) ----------

        // /music/play?id=spotify:playlist:… or ?id=local:track:… — the source is inferred from the id shape.
        music.MapMethods("/play", EndpointHelpers.GetOrPost, async (string id, MusicRouter router) =>
        {
            var source = await router.PlayAsync(id);
            return new { playing = id, source };
        });

        // The rest route through the active source, with an optional ?source=spotify|local override on each.
        // A deliberate pause button should say why it did nothing, so it surfaces a missing device (unlike the
        // best-effort pause used by scene stop / source switching).
        music.MapMethods("/pause", EndpointHelpers.GetOrPost, async (string? source, MusicRouter router) =>
        {
            await router.Resolve(source).PauseAsync();
            return new { music = "paused" };
        });
        music.MapMethods("/resume", EndpointHelpers.GetOrPost, async (string? source, MusicRouter router) =>
        {
            await router.Resolve(source).ResumeAsync();
            return new { music = "playing" };
        });
        music.MapMethods("/next", EndpointHelpers.GetOrPost, async (string? source, MusicRouter router) =>
        {
            await router.Resolve(source).NextAsync();
            return new { music = "next" };
        });
        music.MapMethods("/previous", EndpointHelpers.GetOrPost, async (string? source, MusicRouter router) =>
        {
            await router.Resolve(source).PreviousAsync();
            return new { music = "previous" };
        });

        // /music/volume?value=0.5   (0.0 - 1.0)
        music.MapMethods("/volume", EndpointHelpers.GetOrPost, async (double value, string? source, MusicRouter router) =>
        {
            await router.Resolve(source).SetVolumeAsync(value);
            return new { volume = value };
        });

        music.MapMethods("/shuffle", EndpointHelpers.GetOrPost, async (bool? value, string? source, MusicRouter router) =>
        {
            await router.Resolve(source).SetShuffleAsync(value ?? true);
            return new { shuffle = value ?? true };
        });

        // /music/repeat?mode=playlist   (off | track | playlist)
        music.MapMethods("/repeat", EndpointHelpers.GetOrPost, async (string mode, string? source, MusicRouter router) =>
        {
            if (mode is not ("off" or "track" or "playlist"))
                throw new ValidationException("error.music.repeatMode");
            await router.Resolve(source).SetRepeatAsync(mode);
            return new { repeat = mode };
        });

        // Playback state across sources: the neutral state + which source produced it + which are available.
        music.MapGet("/state", (string? source, MusicRouter router) => router.GetStateAsync(source));

        // ---------- Spotify browser (playlists / search stay Spotify-specific) ----------

        music.MapGet("/playlists", (SpotifyClient spotify) => spotify.GetPlaylistsAsync());
        music.MapGet("/search", (string q, SpotifyClient spotify) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                throw new ValidationException("error.music.searchTerm");
            return spotify.SearchTracksAsync(q);
        });

        // ---------- local music library (bring-your-own files) ----------

        music.MapGet("/library/tracks", async (MusicTrackStore store, MusicFileStorage files) =>
        {
            var all = await store.GetAllAsync();
            // Backfill natural length for any track imported before duration tracking (same lazy, at-most-once
            // pattern as /sounds/list); a file that won't decode persists 0 so we don't re-probe it every list.
            foreach (var track in all.Where(t => t.DurationMs is null))
            {
                track.DurationMs = SoundboardPlayer.TryMeasureDurationMs(files.FullPath(track)) ?? 0;
                try { await store.UpsertAsync(track); } catch { /* leave for next list; not worth failing this one */ }
            }
            return all.Select(ToTrackDto);
        });

        // Import: multipart upload, mirroring /sounds/import (manual form read → no antiforgery requirement;
        // the optional API key still guards the route).
        music.MapPost("/library/import", async (HttpRequest request, MusicImporter importer) =>
        {
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
                cap.MaxRequestBodySize = MaxUploadBytes;

            if (!request.HasFormContentType)
                throw new ValidationException("error.upload.multipartRequired");

            var form = await request.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                throw new ValidationException("error.upload.noFile");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!MusicFileStorage.AllowedExtensions.Contains(ext))
                throw new ValidationException("error.upload.unsupportedType",
                    ext, string.Join(", ", MusicFileStorage.AllowedExtensions));

            var name = Blank(form["name"].ToString()) ?? Path.GetFileNameWithoutExtension(file.FileName);
            var artist = Blank(form["artist"].ToString()) ?? "";

            await using var stream = file.OpenReadStream();
            var track = await importer.ImportAsync(name, artist, ext, stream);
            return Results.Ok(ToTrackDto(track));
        }).DisableAntiforgery();

        music.MapPut("/library/tracks/{id}", async (string id, MusicTrackUpdateInput input, MusicTrackStore store) =>
        {
            if (await store.GetAsync(id) is not { } track)
                throw new NotFoundException("error.notFound.musicTrack", id);
            if (input.Name is not null) track.Name = input.Name.Trim();
            if (input.Artist is not null) track.Artist = input.Artist.Trim();
            MusicValidation.ValidateTrack(track);
            await store.UpsertAsync(track);
            return Results.Ok(ToTrackDto(track));
        });

        music.MapDelete("/library/tracks/{id}", async (string id, MusicTrackStore tracks, MusicPlaylistStore playlists,
            MusicFileStorage files, LocalMusicPlayer player, SceneStore scenes) =>
        {
            if (await tracks.GetAsync(id) is not { } track)
                return Results.NotFound();

            // Release the file first if this exact track is the one currently playing (a live reader holds it).
            if (string.Equals(player.CurrentTrackId, id, StringComparison.OrdinalIgnoreCase))
                player.Stop();
            files.Delete(track.FileName);
            await tracks.DeleteAsync(id);

            // Scrub the id from every playlist and from any scene whose music pointed at local:track:{id}
            // (null the play id, keep the volume — mirroring the sound-delete scrub), so nothing dangles.
            foreach (var pl in await playlists.GetAllAsync())
                if (pl.TrackIds.RemoveAll(t => string.Equals(t, id, StringComparison.OrdinalIgnoreCase)) > 0)
                    await playlists.UpsertAsync(pl);
            await ScrubScenesReferencingAsync(scenes, LocalMusicId.ForTrack(id));
            return Results.NoContent();
        });

        music.MapGet("/library/playlists", async (MusicPlaylistStore store) =>
            (await store.GetAllAsync()).Select(ToPlaylistDto));

        music.MapPut("/library/playlists/{id}", async (string id, MusicPlaylistInput input, MusicPlaylistStore store) =>
        {
            var playlist = new MusicPlaylist
            {
                Id = id,
                Name = input.Name?.Trim() ?? "",
                TrackIds = input.TrackIds ?? [],
            };
            MusicValidation.ValidatePlaylist(playlist);
            await store.UpsertAsync(playlist);
            return Results.Ok(ToPlaylistDto(playlist));
        });

        music.MapDelete("/library/playlists/{id}", async (string id, MusicPlaylistStore store, SceneStore scenes) =>
        {
            if (!await store.DeleteAsync(id))
                return Results.NotFound();
            // Scrub any scene whose music pointed at local:playlist:{id} (null the play id, keep the volume).
            await ScrubScenesReferencingAsync(scenes, LocalMusicId.ForPlaylist(id));
            return Results.NoContent();
        });
    }

    // Null the PlayId (keeping Volume/Source) of every scene whose music pointed at the now-gone reference.
    private static async Task ScrubScenesReferencingAsync(SceneStore scenes, string playId)
    {
        foreach (var scene in await scenes.GetAllAsync())
            if (scene.Music is { } m && string.Equals(m.PlayId, playId, StringComparison.OrdinalIgnoreCase))
            {
                m.PlayId = null;
                await scenes.UpsertAsync(scene);
            }
    }

    private static MusicTrackDto ToTrackDto(Models.MusicTrack t) =>
        new(t.Id, t.Name, t.Artist, t.DurationMs, LocalMusicId.ForTrack(t.Id));

    private static MusicPlaylistDto ToPlaylistDto(Models.MusicPlaylist p) =>
        new(p.Id, p.Name, p.TrackIds, LocalMusicId.ForPlaylist(p.Id));

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
