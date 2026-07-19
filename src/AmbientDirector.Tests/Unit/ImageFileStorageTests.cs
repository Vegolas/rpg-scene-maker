using AmbientDirector.Api.Services;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>
/// The stored-name guard that backs the two file-serving read paths — <c>GET /images/{name}</c> and the
/// player-facing <c>GET /tv/content/current</c> (and the <c>/tv/show</c> validation) — all funnel through
/// <see cref="ImageFileStorage.IsValidName"/> / <see cref="ImageFileStorage.FullPathForName"/>. Sound and
/// music files are never read by a user-supplied name, so this is the one name-driven read chokepoint.
/// </summary>
public class ImageFileStorageTests
{
    [Theory]
    [InlineData("abc123.png")]
    [InlineData("a1b2c3d4e5f6.webp")]
    [InlineData("scene-fire-1.jpg")]
    [InlineData("x.jpeg")]
    public void IsValidName_accepts_a_stored_slug_name(string name) =>
        Assert.True(ImageFileStorage.IsValidName(name));

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("..\\secret.png")]
    [InlineData("foo/bar.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\windows\\x.png")]
    [InlineData("....//x.png")]
    [InlineData("a.exe")]          // not an image extension
    [InlineData("a.svg")]          // SVG is not accepted (script vector)
    [InlineData("UPPER.PNG")]      // ids are generated lowercase; the guard is lowercase-only
    [InlineData("a b.png")]        // no spaces
    [InlineData("")]
    [InlineData(null)]
    public void IsValidName_rejects_traversal_and_junk(string? name) =>
        Assert.False(ImageFileStorage.IsValidName(name));

    [Fact]
    public void FullPathForName_returns_null_for_a_bad_name_and_a_rooted_path_for_a_good_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rsm-imgtest", Guid.NewGuid().ToString("N"));
        var storage = new ImageFileStorage(dir);
        try
        {
            Assert.Null(storage.FullPathForName("../../secret.png"));
            Assert.Null(storage.FullPathForName("evil.svg"));

            var ok = storage.FullPathForName("card-art.jpg");
            Assert.NotNull(ok);
            Assert.StartsWith(dir, ok!);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
