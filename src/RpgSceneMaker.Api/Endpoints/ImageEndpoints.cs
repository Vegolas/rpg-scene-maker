using Microsoft.AspNetCore.Http.Features;
using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Endpoints;

public static class ImageEndpoints
{
    // Cap uploads so a stray huge file can't fill the disk; the browser already crops + downscales tile art.
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public static void MapImageEndpoints(this WebApplication app)
    {
        var images = app.MapGroup("/images");

        // Upload: multipart form with a 'file' part. Read manually (no IFormFile binding → no antiforgery
        // requirement); the optional API key still guards the route. Returns { "id": "<storedFileName>" }.
        images.MapPost("/upload", async (HttpRequest request, ImageFileStorage storage) =>
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
            if (!ImageFileStorage.AllowedExtensions.Contains(ext))
                throw new ValidationException("error.upload.unsupportedType",
                    ext, string.Join(", ", ImageFileStorage.AllowedExtensions));

            // A short random lowercase-hex token (matches the [a-z0-9-] stored-name guard); no name to slugify.
            var id = Guid.NewGuid().ToString("N")[..12];

            await using var stream = file.OpenReadStream();
            var stored = await storage.SaveAsync(id, ext, stream);
            return Results.Ok(new { id = stored });
        }).DisableAntiforgery();

        // Serve raw bytes with the right content-type. GET only (upload is POST-only, like /sounds/import).
        // The name is validated (traversal guard) inside FullPathForName.
        images.MapGet("/{name}", (string name, ImageFileStorage storage) =>
        {
            var full = storage.FullPathForName(name);
            if (full is null || !File.Exists(full))
                return Results.NotFound();
            return Results.File(full, ImageFileStorage.ContentTypeFor(name));
        });
    }
}
