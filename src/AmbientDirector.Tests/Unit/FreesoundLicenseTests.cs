using AmbientDirector.Api.Services;
using Xunit;

namespace AmbientDirector.Tests.Unit;

public class FreesoundLicenseTests
{
    [Theory]
    [InlineData("http://creativecommons.org/publicdomain/zero/1.0/", "CC0 1.0")]
    [InlineData("https://creativecommons.org/publicdomain/zero/1.0/", "CC0 1.0")]
    [InlineData("http://creativecommons.org/licenses/by/4.0/", "CC BY 4.0")]
    [InlineData("https://creativecommons.org/licenses/by/4.0", "CC BY 4.0")]
    [InlineData("http://creativecommons.org/licenses/by/3.0/", "CC BY 3.0")]
    [InlineData("http://creativecommons.org/licenses/by-nc/4.0/", "CC BY-NC 4.0")]
    [InlineData("http://creativecommons.org/licenses/by-nc/3.0/", "CC BY-NC 3.0")]
    [InlineData("http://creativecommons.org/licenses/sampling+/1.0/", "Sampling+")]
    public void Maps_known_freesound_license_urls_to_short_labels(string url, string expected) =>
        Assert.Equal(expected, FreesoundLicense.Label(url));

    [Fact]
    public void Unknown_url_is_returned_verbatim()
    {
        const string url = "https://example.org/some-custom-license";
        Assert.Equal(url, FreesoundLicense.Label(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_maps_to_empty(string? url) =>
        Assert.Equal("", FreesoundLicense.Label(url));

    [Fact]
    public void Trims_whitespace_before_matching() =>
        Assert.Equal("CC BY 4.0", FreesoundLicense.Label("  https://creativecommons.org/licenses/by/4.0/  "));
}
