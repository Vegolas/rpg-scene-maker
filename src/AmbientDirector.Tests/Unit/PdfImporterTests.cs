using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Images;
using AmbientDirector.Tests.Support;
using SkiaSharp;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>
/// The PDF page → image importer (issue #88): page counting, thumbnail + full-page rendering (PDFium +
/// SkiaSharp, exercised here so this doubles as the Linux render proof on CI), the size/range guards, and the
/// traversal guard on the client-supplied temp id. Test PDFs are built in memory (no binary fixture).
/// </summary>
public class PdfImporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rsm-pdftest", Guid.NewGuid().ToString("N"));
    private readonly ImageFileStorage _images;
    private readonly PdfImporter _importer;

    public PdfImporterTests()
    {
        Directory.CreateDirectory(_dir);
        _images = new ImageFileStorage(_dir);
        _importer = new PdfImporter(_dir, _images);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<string> SaveAsync(byte[] pdf)
    {
        var (id, _) = await _importer.SaveTempAsync(new MemoryStream(pdf));
        return id;
    }

    [Fact]
    public async Task SaveTemp_reports_the_page_count()
    {
        var (id, pages) = await _importer.SaveTempAsync(new MemoryStream(TestPdf.Create(pageCount: 2)));

        Assert.Equal(2, pages);
        Assert.Matches("^[a-z0-9]{12}$", id);
    }

    [Fact]
    public async Task RenderThumb_portrait_pins_the_long_edge_to_480_and_preserves_aspect()
    {
        // US Letter portrait 612×792 → the long edge (height) lands exactly on 480 and the width is scaled by
        // the page's real aspect (NOT forced to a 480×480 square).
        var id = await SaveAsync(TestPdf.Create(pageCount: 2, width: 612, height: 792));

        var bytes = _importer.RenderThumb(id, 1);

        Assert.True(bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8, "expected JPEG magic FF D8");
        using var bmp = SKBitmap.Decode(bytes);
        Assert.NotNull(bmp);
        Assert.Equal(480, bmp.Height);            // long edge exactly on the box
        Assert.InRange(bmp.Width, 369, 373);      // ≈ 480·612/792 = 371, ±2 for renderer rounding
    }

    [Fact]
    public async Task RenderThumb_landscape_pins_the_width_and_preserves_aspect()
    {
        // Landscape 792×612 → the long edge is now the width, pinned to 480; the height scales down.
        var id = await SaveAsync(TestPdf.Create(pageCount: 1, width: 792, height: 612));

        var bytes = _importer.RenderThumb(id, 1);

        using var bmp = SKBitmap.Decode(bytes);
        Assert.NotNull(bmp);
        Assert.Equal(480, bmp.Width);             // long edge (width) exactly on the box
        Assert.InRange(bmp.Height, 369, 373);     // ≈ 480·612/792 = 371, ±2
    }

    [Fact]
    public async Task ImportPages_portrait_pins_the_long_edge_to_2200_and_preserves_aspect()
    {
        var id = await SaveAsync(TestPdf.Create(pageCount: 2, width: 612, height: 792));

        var saved = await _importer.ImportPagesAsync(id, [1]);

        var page = Assert.Single(saved);
        Assert.Equal(1, page.Page);

        var full = _images.FullPathForName(page.StoredName);
        Assert.NotNull(full);
        var bytes = await File.ReadAllBytesAsync(full!);
        Assert.True(bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            "expected PNG magic 89 50 4E 47");
        using var bmp = SKBitmap.Decode(bytes);
        Assert.NotNull(bmp);
        Assert.Equal(2200, bmp.Height);           // long edge exactly on the box
        Assert.InRange(bmp.Width, 1698, 1702);    // ≈ 2200·612/792 = 1700, ±2
    }

    [Fact]
    public async Task Garbage_bytes_are_rejected_as_invalid()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _importer.SaveTempAsync(new MemoryStream("this is not a pdf at all"u8.ToArray())));

        Assert.Equal("error.pdf.invalid", ex.Code);
    }

    [Fact]
    public async Task An_out_of_range_page_is_rejected()
    {
        var id = await SaveAsync(TestPdf.Create(pageCount: 2));

        var ex = Assert.Throws<ValidationException>(() => _importer.RenderThumb(id, 99));

        Assert.Equal("error.pdf.pageOutOfRange", ex.Code);
    }

    [Fact]
    public async Task Requesting_more_than_twenty_pages_is_rejected()
    {
        var id = await SaveAsync(TestPdf.Create(pageCount: 2));

        // 21 distinct pages trips the count guard before the range guard (which the 2-page doc would also fail).
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _importer.ImportPagesAsync(id, Enumerable.Range(1, 21).ToList()));

        Assert.Equal("error.pdf.tooManyPages", ex.Code);
    }

    [Fact]
    public async Task Reusing_a_consumed_temp_id_is_notFound()
    {
        var id = await SaveAsync(TestPdf.Create(pageCount: 2));
        await _importer.ImportPagesAsync(id, [1]);   // a successful import deletes the temp (one-shot)

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _importer.ImportPagesAsync(id, [1]));

        Assert.Equal("error.pdf.notFound", ex.Code);
    }

    [Fact]
    public void A_traversal_id_is_notFound_and_never_touches_disk()
    {
        // Guarded by the ^[a-z0-9]{12}$ id check before any Path.Combine, so nothing outside the temp dir is read.
        var ex = Assert.Throws<ValidationException>(() => _importer.RenderThumb("../../evil", 1));

        Assert.Equal("error.pdf.notFound", ex.Code);
    }
}
