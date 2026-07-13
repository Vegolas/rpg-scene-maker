using RpgSceneMaker.Api.Services;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class SpotifyUriTests
{
    [Theory]
    [InlineData("spotify:track:4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("spotify:playlist:37i9dQZF1DX")]
    [InlineData("spotify:album:1DFixLWuPkv3KT3TnV35m3")]
    [InlineData("spotify:artist:0OdUWJ0sBjDrqHygGUXeCF")]
    [InlineData("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DX")]
    [InlineData("https://open.spotify.com/intl-pl/track/4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC?si=abc123")]
    public void IsSpotifyUri_accepts_valid_references(string input) =>
        Assert.True(SpotifyClient.IsSpotifyUri(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("spotify:episode:123")]                 // unsupported type
    [InlineData("spotify:track:")]                      // missing id
    [InlineData("https://open.spotify.com/episode/123")]// unsupported type
    [InlineData("https://open.spotify.com/track")]      // missing id segment
    [InlineData("https://example.com/track/123")]       // wrong host
    [InlineData("just some text")]
    public void IsSpotifyUri_rejects_garbage(string input) =>
        Assert.False(SpotifyClient.IsSpotifyUri(input));

    [Theory]
    [InlineData("spotify:track:ABC", "spotify:track:ABC")]
    [InlineData("SPOTIFY:TRACK:ABC", "spotify:track:ABC")]                                   // type lower-cased
    [InlineData("https://open.spotify.com/track/ABC", "spotify:track:ABC")]
    [InlineData("https://open.spotify.com/playlist/XYZ", "spotify:playlist:XYZ")]
    [InlineData("https://open.spotify.com/intl-pl/album/XYZ", "spotify:album:XYZ")]          // locale segment skipped
    [InlineData("https://open.spotify.com/track/ABC?si=deadbeef", "spotify:track:ABC")]      // query stripped
    public void NormalizeUri_canonicalises(string input, string expected) =>
        Assert.Equal(expected, SpotifyClient.NormalizeUri(input));

    [Fact]
    public void NormalizeUri_throws_on_unrecognised() =>
        Assert.ThrowsAny<ArgumentException>(() => SpotifyClient.NormalizeUri("not a uri"));
}
