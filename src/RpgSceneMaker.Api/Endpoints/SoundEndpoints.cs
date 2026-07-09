using System.Text;
using Microsoft.AspNetCore.Http.Features;
using RpgSceneMaker.Api.Contracts;
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

        sounds.MapGet("/list", async (SoundStore store) =>
            (await store.GetAllAsync()).Select(ToDto));

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
                throw new ArgumentException("Upload must be multipart/form-data with a 'file' field.");

            var form = await request.ReadFormAsync();
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                throw new ArgumentException("No file uploaded (expected a 'file' field).");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!SoundFileStorage.AllowedExtensions.Contains(ext))
                throw new ArgumentException(
                    $"Unsupported file type '{ext}'. Allowed: {string.Join(", ", SoundFileStorage.AllowedExtensions)}.");

            var name = Blank(form["name"].ToString()) ?? Path.GetFileNameWithoutExtension(file.FileName);
            var category = Blank(form["category"].ToString()) ?? "";
            var id = await UniqueIdAsync(name, store);

            await using var stream = file.OpenReadStream();
            var storedName = await files.SaveAsync(id, ext, stream);
            var sound = new Sound { Id = id, Name = name, Category = category, FileName = storedName };
            SoundValidation.Validate(sound);
            await store.UpsertAsync(sound);
            return Results.Ok(ToDto(sound));
        }).DisableAntiforgery();

        sounds.MapPut("/{id}", async (string id, SoundUpdateInput input, SoundStore store) =>
        {
            if (await store.GetAsync(id) is not { } sound)
                return Results.NotFound(new { error = $"No sound with id '{id}'." });
            if (input.Name is not null) sound.Name = input.Name.Trim();
            if (input.Category is not null) sound.Category = input.Category.Trim();
            if (input.Volume is { } volume) sound.Volume = volume;
            if (input.Loop is { } loop) sound.Loop = loop;
            SoundValidation.Validate(sound);
            await store.UpsertAsync(sound);
            return Results.Ok(ToDto(sound));
        });

        sounds.MapDelete("/{id}", async (string id, SoundStore store, SoundFileStorage files, SoundboardPlayer player, SceneStore scenes) =>
        {
            if (await store.GetAsync(id) is not { } sound)
                return Results.NotFound();
            player.Stop(id);
            files.Delete(sound.FileName);
            await store.DeleteAsync(id);

            // Drop the now-gone sound from any scene that referenced it, so activating those scenes
            // no longer logs "sound(s) not found and skipped" for a dangling id.
            foreach (var scene in await scenes.GetAllAsync())
            {
                var effects = scene.SoundEffects;
                if (effects is null || effects.Count == 0) continue;
                var kept = effects.Where(sid => !string.Equals(sid, sound.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                if (kept.Count != effects.Count)
                {
                    scene.SoundEffects = kept;
                    await scenes.UpsertAsync(scene);
                }
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

    private static SoundDto ToDto(Sound s) => new(s.Id, s.Name, s.Category, s.Volume, s.Loop);

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
