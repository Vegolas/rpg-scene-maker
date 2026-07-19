using Microsoft.AspNetCore.Http.Features;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Images;

namespace AmbientDirector.Api.Endpoints;

public static class ImageEndpoints
{
    // Cap uploads/imports so a stray huge file can't fill the disk; the browser already crops + downscales
    // tile art, and imported card art (Scryfall art_crop) is small.
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    // A source PDF may be a whole handout/rulebook, so it gets a larger cap than a tile image. 25 MB is the
    // validated limit (→ error.pdf.tooLarge); the per-request body cap is a touch higher so multipart framing
    // overhead can't trip Kestrel's 413 before our own check runs.
    private const long MaxPdfBytes = 25 * 1024 * 1024;
    private const long MaxPdfRequestBytes = 26 * 1024 * 1024;

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

        // List the registered image sources (id + display name + English attribution) for the picker.
        images.MapGet("/sources", (IEnumerable<IImageSearchSource> sources) =>
            Results.Ok(sources.Select(s => new ImageSourceDto(s.Id, s.Name, s.Attribution)).ToList()));

        // Search one source and return a page of hits: { source, total, hasMore, results[] }. GET only — a
        // literal route, so it beats the GET /images/{name} byte server. The panel calls it directly.
        images.MapGet("/search", async (string? source, string? q,
            IEnumerable<IImageSearchSource> sources, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                throw new ValidationException("error.imageSearch.queryRequired");
            var picked = ResolveSource(sources, source);
            return Results.Ok(await picked.SearchAsync(q, ct));
        });

        // Import: fetch a picked image URL server-side (allowlisted source host only) and store it like
        // /upload, returning { "id": "<storedFileName>" }. POST-only (not a Stream-Deck command endpoint).
        images.MapPost("/import", async (ImageImportInput? input,
            IEnumerable<IImageSearchSource> sources, ImageFileStorage storage, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input?.Url))
                throw new ValidationException("error.imageImport.urlRequired");

            // Only ever fetch an absolute https URL that a registered source vouches for (its own CDN host).
            if (!Uri.TryCreate(input.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                throw new ValidationException("error.imageImport.hostNotAllowed");
            var source = sources.FirstOrDefault(s => s.CanFetch(url))
                ?? throw new ValidationException("error.imageImport.hostNotAllowed");

            using var response = await source.FetchImageAsync(url, ct);

            // Redirect guard: whatever host we actually landed on (after any redirects) must still be allowed.
            var finalUrl = response.RequestMessage?.RequestUri;
            if (finalUrl is null || !source.CanFetch(finalUrl))
                throw new ValidationException("error.imageImport.hostNotAllowed");
            if (!response.IsSuccessStatusCode)
                throw new ImageSourceException($"Image download from {url.Host} failed ({(int)response.StatusCode}).");

            var ext = ResolveExtension(response, url);

            // Reject early when the server declares an over-cap length…
            if (response.Content.Headers.ContentLength is > MaxUploadBytes)
                throw new ValidationException("error.imageImport.tooLarge", "10");

            // …and always bound the actual copy so a missing/lying Content-Length can't blow past the cap.
            var id = Guid.NewGuid().ToString("N")[..12];
            try
            {
                await using var upstream = await response.Content.ReadAsStreamAsync(ct);
                var capped = new CappedStream(upstream, MaxUploadBytes);
                var stored = await storage.SaveAsync(id, ext, capped, ct);
                return Results.Ok(new { id = stored });
            }
            catch
            {
                storage.Delete(id + ext);   // drop any partially written file on a cap breach / read error
                throw;
            }
        });

        // ---- PDF page → image import (issue #88) ----
        // Upload a PDF (held only as a short-lived temp), browse page thumbnails, then import a picked page as
        // an ordinary stored image. No PDF is ever persisted; imported pages then work everywhere images do.

        // Upload: multipart 'file' part like /upload, but PDF-sized. PDFium parsing IS the validity check, so
        // we don't gate on extension/content-type — only the size cap. Returns { id, pages }.
        images.MapPost("/pdf/upload", async (HttpRequest request, PdfImporter pdf, CancellationToken ct) =>
        {
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
                cap.MaxRequestBodySize = MaxPdfRequestBytes;

            if (!request.HasFormContentType)
                throw new ValidationException("error.upload.multipartRequired");

            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                throw new ValidationException("error.upload.noFile");
            if (file.Length > MaxPdfBytes)
                throw new ValidationException("error.pdf.tooLarge", "25");

            await using var stream = file.OpenReadStream();
            var (id, pages) = await pdf.SaveTempAsync(stream, ct);
            return Results.Ok(new PdfUploadResultDto(id, pages));
        }).DisableAntiforgery();

        // Thumbnail for one page (1-based). A literal multi-segment route, so it can't collide with the
        // GET /{name} byte server below. Temp ids are unique per upload, so the rendered thumb is safe to
        // cache privately for the temp's lifetime.
        images.MapGet("/pdf/{id}/thumb/{page:int}", (string id, int page, PdfImporter pdf, HttpResponse response) =>
        {
            var bytes = pdf.RenderThumb(id, page);
            response.Headers.CacheControl = "private, max-age=3600";
            return Results.Bytes(bytes, "image/jpeg");
        });

        // Import picked pages (1-based) into stored images; returns one { page, id } per page. POST-only (not a
        // Stream-Deck command endpoint, same rationale as /images/import).
        images.MapPost("/pdf/{id}/import", async (string id, PdfImportRequest? body, PdfImporter pdf, CancellationToken ct) =>
        {
            var saved = await pdf.ImportPagesAsync(id, body?.Pages ?? [], ct);
            return Results.Ok(saved.Select(p => new PdfImportedPageDto(p.Page, p.StoredName)).ToList());
        });

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

    // Resolve the ?source= id to a registered source. Forgiving: with source omitted and exactly one
    // registered, use it; otherwise a missing/unknown id is a 400.
    private static IImageSearchSource ResolveSource(IEnumerable<IImageSearchSource> sources, string? id)
    {
        var list = sources as IReadOnlyList<IImageSearchSource> ?? sources.ToList();
        if (string.IsNullOrWhiteSpace(id))
            return list.Count == 1 ? list[0] : throw new ValidationException("error.imageSearch.unknownSource", id ?? "");
        return list.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException("error.imageSearch.unknownSource", id);
    }

    // Pick the stored extension from the download's Content-Type, falling back to the URL's own extension
    // when the type is missing or a generic octet-stream; reject anything we don't store as tile art.
    private static string ResolveExtension(HttpResponseMessage response, Uri url)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        switch (contentType)
        {
            case "image/jpeg": return ".jpg";
            case "image/png": return ".png";
            case "image/webp": return ".webp";
        }
        if (string.IsNullOrEmpty(contentType) || contentType == "application/octet-stream")
        {
            var urlExt = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
            if (ImageFileStorage.AllowedExtensions.Contains(urlExt))
                return urlExt;
        }
        throw new ValidationException("error.imageImport.unsupportedType", contentType ?? "");
    }
}

// Read-only stream wrapper that throws once more than the cap has been read, so a copy can't exceed the
// cap even when the upstream Content-Length is missing or lies. Used by POST /images/import.
file sealed class CappedStream(Stream inner, long cap) : Stream
{
    private long _read;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        Count(n);
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = inner.Read(buffer, offset, count);
        Count(n);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Count(int n)
    {
        if (n <= 0) return;
        _read += n;
        if (_read > cap)
            throw new ValidationException("error.imageImport.tooLarge", "10");
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
