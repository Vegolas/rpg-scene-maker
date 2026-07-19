using System.Text.RegularExpressions;
using AmbientDirector.Api.Errors;
using PDFtoImage;
using SkiaSharp;

namespace AmbientDirector.Api.Services.Images;

/// <summary>Renders a picked PDF page into an ordinary stored image (issue #88). The GM uploads a PDF —
/// held only as a short-lived temp under <c>&lt;images&gt;/.pdf-tmp</c> — browses page thumbnails, and picks
/// pages; each picked page is rendered full-quality and saved through <see cref="ImageFileStorage"/> like any
/// other tile art. No PDF is ever persisted (the temp is discarded after import, and swept after an hour), so
/// imported pages then work everywhere images already do (tile art, /tv/show). Rendering is PDFium + SkiaSharp
/// via the PDFtoImage package. Pages are <b>1-based</b> across this whole surface (the panel shows "Page 1");
/// PDFtoImage's <see cref="System.Index"/> is 0-based, converted internally.</summary>
public class PdfImporter
{
    // Thumbnails fit within this box; full imports within the larger one — both aspect-preserved.
    private const int ThumbMaxEdge = 480;
    private const int ImportMaxEdge = 2200;
    // Distinct pages importable in one call — a board is a handful of pages, not a whole rulebook.
    private const int MaxImportPages = 20;
    // A page whose PNG lands over this is photo-heavy (a scanned map, not line art); re-encode it as JPEG so a
    // stored tile stays a sane size. 10 MB matches the ImageFileStorage upload cap.
    private const long PngToJpegThreshold = 10 * 1024 * 1024;
    // Temps older than this are swept on the next upload — a picked page is imported within one sitting.
    private static readonly TimeSpan TempTtl = TimeSpan.FromHours(1);

    // A temp id is 12 lowercase-hex chars. It is echoed to the client and comes back as user input on
    // thumb/import, so this guard runs BEFORE any Path.Combine — the traversal guard (same spirit as
    // ImageFileStorage.ValidName): no '..', slash or drive letter can match.
    private static readonly Regex ValidId = new("^[a-z0-9]{12}$", RegexOptions.Compiled);

    // PDFium's thread-safety is not guaranteed; funnel every PDFium call through one lock. Table traffic is a
    // single GM, so contention is a non-issue.
    private readonly object _pdfium = new();
    private readonly string _tempDir;
    private readonly ImageFileStorage _images;

    public PdfImporter(string imagesPath, ImageFileStorage images)
    {
        _images = images;
        _tempDir = Path.Combine(imagesPath, ".pdf-tmp");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>Persist an uploaded PDF as a temp and return its id + page count. Sweeps stale temps first,
    /// then validates the file through PDFium — a file that can't be read as a PDF (or has no pages) is
    /// deleted and rejected as <c>error.pdf.invalid</c>.</summary>
    public async Task<(string Id, int PageCount)> SaveTempAsync(Stream pdf, CancellationToken ct = default)
    {
        SweepStaleTemps();

        var id = Guid.NewGuid().ToString("N")[..12];     // freshly generated here — safe by construction
        var path = Path.Combine(_tempDir, id + ".pdf");
        await using (var file = File.Create(path))
            await pdf.CopyToAsync(file, ct);

        int pageCount;
        try
        {
            pageCount = PageCount(path);
        }
        catch
        {
            TryDelete(path);
            throw new ValidationException("error.pdf.invalid");
        }
        if (pageCount <= 0)
        {
            TryDelete(path);
            throw new ValidationException("error.pdf.invalid");
        }
        return (id, pageCount);
    }

    /// <summary>Render one page (1-based) to a JPEG thumbnail scaled to fit within 480×480 (aspect preserved).</summary>
    public byte[] RenderThumb(string id, int page)
    {
        var path = ResolveTemp(id);
        var count = PageCount(path);
        if (page < 1 || page > count)
            throw new ValidationException("error.pdf.pageOutOfRange", page, count);
        using var bitmap = RenderPage(path, page, ThumbMaxEdge);
        return Encode(bitmap, SKEncodedImageFormat.Jpeg, quality: 80);
    }

    /// <summary>Render each picked page (1-based) full-quality and store it via <see cref="ImageFileStorage"/>,
    /// returning the (page, stored-name) pairs. Pages render to fit within 2200×2200 as PNG; a page whose PNG
    /// is photo-heavy (over the 10 MB cap) is re-encoded as JPEG instead. On success the temp PDF is deleted
    /// (one-shot import — the picker closes after). If a page fails mid-loop, the images already saved by THIS
    /// call are deleted and the temp PDF is kept so the caller can retry.</summary>
    public async Task<List<PdfImportedPage>> ImportPagesAsync(string id, List<int> pages, CancellationToken ct = default)
    {
        if (pages is null || pages.Count == 0)
            throw new ValidationException("error.pdf.noPages");
        var distinct = pages.Distinct().ToList();
        if (distinct.Count > MaxImportPages)
            throw new ValidationException("error.pdf.tooManyPages", MaxImportPages);

        var path = ResolveTemp(id);
        var count = PageCount(path);
        foreach (var page in distinct)
            if (page < 1 || page > count)
                throw new ValidationException("error.pdf.pageOutOfRange", page, count);

        var saved = new List<PdfImportedPage>();
        try
        {
            foreach (var page in distinct)
            {
                ct.ThrowIfCancellationRequested();
                using var bitmap = RenderPage(path, page, ImportMaxEdge);

                var png = Encode(bitmap, SKEncodedImageFormat.Png, quality: 100);
                string ext;
                byte[] bytes;
                if (png.LongLength > PngToJpegThreshold)
                {
                    ext = ".jpg";
                    bytes = Encode(bitmap, SKEncodedImageFormat.Jpeg, quality: 85);
                }
                else
                {
                    ext = ".png";
                    bytes = png;
                }

                var newId = Guid.NewGuid().ToString("N")[..12];
                using var ms = new MemoryStream(bytes, writable: false);
                var stored = await _images.SaveAsync(newId, ext, ms, ct);
                saved.Add(new PdfImportedPage(page, stored));
            }
        }
        catch
        {
            foreach (var page in saved) _images.Delete(page.StoredName);   // undo this call's writes; keep the temp
            throw;
        }

        TryDelete(path);
        return saved;
    }

    // ---- PDFium/render internals: every PDFium call is under _pdfium; a fresh FileStream per call (we don't
    //      assume leaveOpen semantics), left open so this method owns disposal. ----
    //
    // CA1416: PDFtoImage annotates these methods for Windows/Linux/macOS (+ mobile) — every OS this self-hosted
    // server actually runs on — but the analyzer can't prove that from the platform-neutral net10.0 TFM, so it
    // warns. Suppressed here for the two PDFium call sites only (the SkiaSharp encode below is unrestricted).
#pragma warning disable CA1416
    private int PageCount(string path)
    {
        lock (_pdfium)
        {
            using var fs = File.OpenRead(path);
            return Conversion.GetPageCount(fs, leaveOpen: true);
        }
    }

    private SKBitmap RenderPage(string path, int page, int maxEdge)
    {
        lock (_pdfium)
        {
            // WithAspectRatio only derives the missing dimension when exactly ONE of Width/Height is set —
            // with BOTH set PDFtoImage returns that exact (square) size and ignores the aspect. So measure the
            // page first and pin its long edge to maxEdge, letting WithAspectRatio scale the short edge; the
            // output is then maxEdge on the long edge with the page's real aspect preserved. 1-based on our
            // surface → PDFtoImage's 0-based Index; a white background so document pages aren't transparent.
            // A fresh FileStream per PDFium call (we don't assume leaveOpen), left open so we own disposal.
            System.Drawing.SizeF size;
            using (var probe = File.OpenRead(path))
                size = Conversion.GetPageSize(probe, page: page - 1, leaveOpen: true);

            var options = size.Height >= size.Width
                ? new RenderOptions(Height: maxEdge, WithAspectRatio: true, BackgroundColor: SKColors.White)
                : new RenderOptions(Width: maxEdge, WithAspectRatio: true, BackgroundColor: SKColors.White);

            using var fs = File.OpenRead(path);
            return Conversion.ToImage(fs, page: page - 1, leaveOpen: true, options: options);
        }
    }
#pragma warning restore CA1416

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality)
            ?? throw new ValidationException("error.pdf.invalid");
        return data.ToArray();
    }

    // Guard the client-supplied id, then require the temp to still exist (unknown / invalid / expired all
    // surface as the same "upload again" error — we never reveal whether a given id ever existed).
    private string ResolveTemp(string id)
    {
        if (!ValidId.IsMatch(id))
            throw new ValidationException("error.pdf.notFound");
        var path = Path.Combine(_tempDir, id + ".pdf");
        if (!File.Exists(path))
            throw new ValidationException("error.pdf.notFound");
        return path;
    }

    private void SweepStaleTemps()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TempTtl;
            foreach (var file in Directory.EnumerateFiles(_tempDir, "*.pdf"))
            {
                try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
                catch { /* locked / racing delete — a leftover temp is harmless */ }
            }
        }
        catch { /* best-effort sweep; never fail an upload over housekeeping */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* a leftover temp is harmless — it's swept an hour later */ }
    }
}

/// <summary>One rendered-and-stored page: its 1-based number and the stored image file name.</summary>
public readonly record struct PdfImportedPage(int Page, string StoredName);
