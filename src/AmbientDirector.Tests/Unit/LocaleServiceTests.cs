using AmbientDirector.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>
/// <see cref="LocaleService.Get"/> resolves the on-disk locale file case-insensitively, so a request for
/// <c>DE</c> still finds <c>de.json</c> on a case-sensitive filesystem (Linux/macOS) — matching how the
/// embedded-resource lookup and <see cref="LocaleService.List"/> already compare codes (#83). On Windows's
/// case-insensitive filesystem the exact-match fast path already covers this; on the Linux CI runner these
/// exercise the case-insensitive fallback directly.
/// </summary>
public class LocaleServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "ad-localetest", Guid.NewGuid().ToString("N"));

    private LocaleService NewService()
    {
        Directory.CreateDirectory(_dir);
        return new LocaleService(_dir, NullLogger<LocaleService>.Instance);
    }

    // A community language (no embedded counterpart) so the on-disk file is the ONLY source: if the
    // case-insensitive lookup missed it, Get would return null rather than falling back to embedded.
    private void WriteCommunityFile(string fileNameCode) =>
        File.WriteAllText(
            Path.Combine(_dir, fileNameCode + ".json"),
            """{ "name": "Deutsch", "englishName": "German", "strings": { "nav.scenes": "Szenen" } }""");

    [Theory]
    [InlineData("de")]   // exact case
    [InlineData("DE")]   // upper
    [InlineData("De")]   // mixed
    public void Get_finds_a_community_file_regardless_of_requested_case(string requested)
    {
        var svc = NewService();
        WriteCommunityFile("de");

        var doc = svc.Get(requested);

        Assert.NotNull(doc);
        Assert.Equal("Deutsch", doc!.Name);
        Assert.Equal("Szenen", doc.Strings["nav.scenes"]);
    }

    [Fact]
    public void Get_overlays_a_case_mismatched_on_disk_shipped_file_onto_the_embedded_base()
    {
        var svc = NewService();
        // Shipped code (en ships embedded); the on-disk file uses a different case than the request and
        // overrides one key. The disk overlay must still be found and win for that key.
        File.WriteAllText(
            Path.Combine(_dir, "en.json"),
            """{ "strings": { "nav.scenes": "OVERRIDDEN" } }""");

        var doc = svc.Get("EN");

        Assert.NotNull(doc);
        Assert.Equal("OVERRIDDEN", doc!.Strings["nav.scenes"]);
        // A key only the embedded base has still resolves (proves it's a merge, not a disk-only replace).
        Assert.True(doc.Strings.Count > 1);
    }

    [Fact]
    public void Get_returns_null_for_an_unknown_community_code()
    {
        var svc = NewService();
        Assert.Null(svc.Get("zz"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
