using System.Text;
using Microsoft.AspNetCore.Http.Features;
using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Endpoints;

public static class SoundEndpoints
{
    // Cap uploads so a stray huge file can't fill the disk; generous enough for long ambience loops.
    private const long MaxUploadBytes = 50 * 1024 * 1024;

    public static void MapSoundEndpoints(this WebApplication app)
    {
        // Nothing is mapped at the bare "/sounds" path: the Blazor panel's Sounds tab lives there, so a
        // full-page load of /sounds must fall through to index.html (same reason /lights uses /lights/list).
        var sounds = app.MapGroup("/sounds");

        sounds.MapGet("/list", async (SoundStore store, SoundFileStorage files) =>
        {
            var all = await store.GetAllAsync();
            // Backfill natural length + waveform for any sound imported before that tracking existed; persist
            // what we can, but never fail the list. A file that won't decode persists the sentinels (0 for
            // duration, empty array for the waveform) so the null-filter converges and we don't re-probe it on
            // every list. This runs at most once per sound (the first list after an upgrade); decoding a large
            // library's audio here is a one-time cost, after which every list is cheap again.
            foreach (var sound in all.Where(s => s.DurationMs is null || s.Waveform is null))
            {
                var path = files.FullPath(sound);
                sound.DurationMs ??= SoundboardPlayer.TryMeasureDurationMs(path) ?? 0;
                sound.Waveform ??= SoundboardPlayer.TryComputeWaveform(path) ?? [];
                try { await store.UpsertAsync(sound); } catch { /* leave for next list; not worth failing this one */ }
            }
            return all.Select(ToDto);
        });

        // Live playback state for the panel poll. Declared before "/{id}" routes; the literal wins anyway.
        sounds.MapGet("/state", (SoundboardPlayer player) => new SoundStateDto(player.PlayingIds));

        // Import: multipart upload. The form is read manually (no IFormFile binding → no antiforgery
        // requirement); the optional API key still guards the route.
        sounds.MapPost("/import", async (HttpRequest request, SoundStore store, SoundFileStorage files) =>
        {
            // Raise the per-request body cap (Kestrel defaults to 30 MB) before touching the body.
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
                cap.MaxRequestBodySize = MaxUploadBytes;

            if (!request.HasFormContentType)
                throw new ValidationException("error.upload.multipartRequired");

            var form = await request.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                throw new ValidationException("error.upload.noFile");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!SoundFileStorage.AllowedExtensions.Contains(ext))
                throw new ValidationException("error.upload.unsupportedType",
                    ext, string.Join(", ", SoundFileStorage.AllowedExtensions));

            var name = Blank(form["name"].ToString()) ?? Path.GetFileNameWithoutExtension(file.FileName);
            var category = Blank(form["category"].ToString()) ?? "";
            var id = await UniqueIdAsync(name, store);

            await using var stream = file.OpenReadStream();
            var storedName = await files.SaveAsync(id, ext, stream);
            var sound = new Sound { Id = id, Name = name, Category = category, FileName = storedName };
            // Measure the file's natural length + waveform preview now (same reader logic as playback); the
            // "unmeasurable" sentinels (0 / empty array) if it won't decode, so /sounds/list never re-probes it.
            var fullPath = files.FullPath(sound);
            sound.DurationMs = SoundboardPlayer.TryMeasureDurationMs(fullPath) ?? 0;
            sound.Waveform = SoundboardPlayer.TryComputeWaveform(fullPath) ?? [];
            SoundValidation.Validate(sound);
            await store.UpsertAsync(sound);
            return Results.Ok(ToDto(sound));
        }).DisableAntiforgery();

        sounds.MapPut("/{id}", async (string id, SoundUpdateInput input, SoundStore store, ImageFileStorage images) =>
        {
            if (await store.GetAsync(id) is not { } sound)
                return Results.NotFound(new { error = $"No sound with id '{id}'." });
            var oldImage = sound.Image;
            if (input.Name is not null) sound.Name = input.Name.Trim();
            if (input.Category is not null) sound.Category = input.Category.Trim();
            if (input.Volume is { } volume) sound.Volume = volume;
            if (input.Loop is { } loop) sound.Loop = loop;
            // Tile art is set as sent (null clears it), not left-unchanged like the fields above.
            sound.Image = input.Image;
            SoundValidation.Validate(sound);
            await store.UpsertAsync(sound);
            // Drop the previous tile art if it was replaced or cleared, so old uploads don't pile up on disk.
            if (!string.IsNullOrEmpty(oldImage) && !string.Equals(oldImage, sound.Image, StringComparison.OrdinalIgnoreCase))
                images.Delete(oldImage);
            return Results.Ok(ToDto(sound));
        });

        sounds.MapDelete("/{id}", async (string id, SoundStore store, SoundFileStorage files, ImageFileStorage images, SoundboardPlayer player, SceneStore scenes, EventStore events) =>
        {
            if (await store.GetAsync(id) is not { } sound)
                return Results.NotFound();
            player.Stop(id);
            files.Delete(sound.FileName);
            images.Delete(sound.Image);
            await store.DeleteAsync(id);

            // Drop the now-gone sound from any scene or event that referenced it, so triggering those
            // no longer logs "sound(s) not found and skipped" for a dangling id.
            foreach (var scene in await scenes.GetAllAsync())
            {
                if (Scrub(scene.SoundEffects, sound.Id) is { } kept)
                {
                    scene.SoundEffects = kept;
                    await scenes.UpsertAsync(scene);
                }
            }
            foreach (var evt in await events.GetAllAsync())
            {
                var dirty = false;
                if (Scrub(evt.SoundEffects, sound.Id) is { } kept)
                {
                    evt.SoundEffects = kept;
                    dirty = true;
                }
                // Also drop any timeline sound clip that referenced the now-gone sound.
                if (evt.Timeline is { } timeline)
                {
                    var keptClips = timeline.Sounds
                        .Where(c => !string.Equals(c.SoundId, sound.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (keptClips.Count != timeline.Sounds.Count)
                    {
                        timeline.Sounds = keptClips;
                        dirty = true;
                        // A timeline stripped of all clips fails validation on the next round-trip and
                        // triggers as a silent no-op — drop it back to a legacy (null-timeline) event.
                        if (timeline.Sounds.Count == 0 && timeline.Lights.Count == 0)
                            evt.Timeline = null;
                    }
                }
                if (dirty)
                    await events.UpsertAsync(evt);
            }
            return Results.NoContent();
        });

        // /sounds/{id}/play?volume=0.8 — volume optional, defaults to the sound's stored volume.
        sounds.MapMethods("/{id}/play", EndpointHelpers.GetOrPost,
            async (string id, double? volume, SoundStore store, SoundFileStorage files, SoundboardPlayer player) =>
        {
            if (await store.GetAsync(id) is not { } sound)
                return Results.NotFound(new { error = $"No sound with id '{id}'. See GET /sounds." });
            player.Play(sound.Id, files.FullPath(sound), sound.Loop, volume ?? sound.Volume);
            return Results.Ok(new { playing = sound.Id });
        });

        sounds.MapMethods("/{id}/stop", EndpointHelpers.GetOrPost, (string id, SoundboardPlayer player) =>
        {
            player.Stop(id);
            return new { stopped = id };
        });

        sounds.MapMethods("/stop", EndpointHelpers.GetOrPost, (SoundboardPlayer player) =>
        {
            player.StopAll();
            return new { stopped = "all" };
        });
    }

    // The list with soundId removed (case-insensitively), or null when it wasn't referenced (no rewrite needed).
    private static List<string>? Scrub(List<string>? soundEffects, string soundId)
    {
        if (soundEffects is null || soundEffects.Count == 0) return null;
        var kept = soundEffects.Where(sid => !string.Equals(sid, soundId, StringComparison.OrdinalIgnoreCase)).ToList();
        return kept.Count != soundEffects.Count ? kept : null;
    }

    // An empty waveform (the "couldn't decode" sentinel) is sent as null — the UI treats both as "no waveform".
    private static SoundDto ToDto(Sound s) =>
        new(s.Id, s.Name, s.Category, s.Volume, s.Loop, s.Image, s.DurationMs, s.Waveform is { Length: > 0 } ? s.Waveform : null);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Slugify the name, then suffix -2, -3, … until it's free (ids are the /sounds/{id}/… URL segment).
    private static async Task<string> UniqueIdAsync(string name, SoundStore store)
    {
        var baseSlug = Slugify(name);
        if (baseSlug.Length == 0) baseSlug = "sound";
        var id = baseSlug;
        for (var n = 2; await store.GetAsync(id) is not null; n++)
            id = $"{baseSlug}-{n}";
        return id;
    }

    private static string Slugify(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
