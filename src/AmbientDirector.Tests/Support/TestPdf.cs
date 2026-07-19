using System.Text;

namespace AmbientDirector.Tests.Support;

/// <summary>
/// Builds a minimal, valid PDF in memory so the PDF-import tests need no checked-in binary fixture. The
/// document is assembled object-by-object with a correct cross-reference table (byte offsets computed as we
/// write), which PDFium parses directly. Pages are blank (a <c>/Page</c> with a <c>/MediaBox</c> and no
/// content) — enough to exercise page counting and rendering.
/// </summary>
public static class TestPdf
{
    /// <param name="pageCount">Number of blank pages (≥ 1).</param>
    /// <param name="width">Page width in PDF points (default US Letter, 612 = 8.5in).</param>
    /// <param name="height">Page height in PDF points (default US Letter, 792 = 11in).</param>
    public static byte[] Create(int pageCount = 1, int width = 612, int height = 792)
    {
        if (pageCount < 1) throw new ArgumentOutOfRangeException(nameof(pageCount));

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        // Header + a binary-marker comment (four bytes ≥ 128) so tools treat the file as binary.
        Write("%PDF-1.4\n");
        ms.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        // Objects: 1 = Catalog, 2 = Pages, 3..(2+pageCount) = one Page each. Record each object's byte offset
        // (from the start of file) as it is written, for the xref table.
        var offsets = new long[pageCount + 2];

        offsets[0] = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var kids = string.Join(" ", Enumerable.Range(3, pageCount).Select(i => $"{i} 0 R"));
        offsets[1] = ms.Position;
        Write($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\nendobj\n");

        for (var i = 0; i < pageCount; i++)
        {
            offsets[2 + i] = ms.Position;
            Write($"{3 + i} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] >>\nendobj\n");
        }

        // Cross-reference table. Object 0 is the free-list head; every entry is exactly 20 bytes
        // (10-digit offset, space, 5-digit generation, space, type, 2-byte EOL).
        var xrefPos = ms.Position;
        var size = pageCount + 3;
        Write($"xref\n0 {size}\n");
        Write("0000000000 65535 f\r\n");
        foreach (var offset in offsets)
            Write($"{offset:D10} 00000 n\r\n");

        Write($"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF");
        return ms.ToArray();
    }
}
