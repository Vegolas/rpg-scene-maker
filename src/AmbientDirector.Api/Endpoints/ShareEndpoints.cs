using Microsoft.AspNetCore.Http.Features;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services.Sharing;

namespace AmbientDirector.Api.Endpoints;

/// <summary>Export/import of shareable content packs (issue #111). Export streams a self-contained <c>.zip</c>;
/// import is two-phase (inspect → commit) so the panel can show a light-key remap step in between. The whole
/// <c>/share</c> prefix is behind the optional API-key gate (see <c>IsProtectedPath</c> in Program.cs).</summary>
public static class ShareEndpoints
{
    public static void MapShareEndpoints(this WebApplication app)
    {
        var share = app.MapGroup("/share");

        // Download a pack. GET like /setup/backup — the key gate covers it and a browser can append ?apiKey=.
        // The temp zip is streamed and deleted as the response stream disposes (FileOptions.DeleteOnClose).
        share.MapGet("/{kind}/{id}/export", async (string kind, string id, ShareExporter exporter) =>
        {
            var result = await exporter.ExportAsync(kind, id);
            var stream = new FileStream(result.TempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            return Results.File(stream, "application/zip", result.FileName);
        });

        // Inspect an uploaded pack (multipart 'file'): what's inside + the light keys to remap, without
        // committing. Manual form read (no IFormFile binding → no antiforgery need); the key gate still guards it.
        share.MapPost("/import/inspect", async (HttpRequest request, ShareImporter importer, CancellationToken ct) =>
        {
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
                cap.MaxRequestBodySize = ShareImporter.MaxRequestBytes;
            if (!request.HasFormContentType)
                throw new ValidationException("error.upload.multipartRequired");

            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                throw new ValidationException("error.upload.noFile");
            if (file.Length > ShareImporter.MaxPackBytes)
                throw new ValidationException("error.share.tooLarge", ShareImporter.MaxPackMb);

            var lang = request.Headers["X-Ui-Lang"].FirstOrDefault();
            await using var stream = file.OpenReadStream();
            return Results.Ok(await importer.SaveTempAndInspectAsync(stream, lang, ct));
        }).DisableAntiforgery();

        // Commit a previously-inspected pack: apply the light mapping and recreate everything as fresh copies.
        share.MapPost("/import/commit", async (HttpRequest request, ShareCommitInput input, ShareImporter importer, CancellationToken ct) =>
        {
            var lang = request.Headers["X-Ui-Lang"].FirstOrDefault();
            return Results.Ok(await importer.CommitAsync(input, lang, ct));
        });
    }
}
