using AmbientDirector.Api.Services.Music;
using Xunit;

namespace AmbientDirector.Tests.Unit;

public class LocalMusicIdTests
{
    [Theory]
    [InlineData("local:track:tavern", true)]
    [InlineData("local:playlist:combat", true)]
    [InlineData("LOCAL:TRACK:tavern", true)]
    [InlineData("spotify:track:abc", false)]
    [InlineData("https://open.spotify.com/track/abc", false)]
    [InlineData("", false)]
    public void IsLocal_detects_the_scheme(string id, bool expected) =>
        Assert.Equal(expected, LocalMusicId.IsLocal(id));

    [Fact]
    public void Builders_produce_the_self_describing_ids()
    {
        Assert.Equal("local:track:tavern", LocalMusicId.ForTrack("tavern"));
        Assert.Equal("local:playlist:combat", LocalMusicId.ForPlaylist("combat"));
    }

    [Theory]
    [InlineData("local:track:tavern", "track", "tavern")]
    [InlineData("local:playlist:combat", "playlist", "combat")]
    public void TryParse_splits_kind_and_id(string id, string kind, string entityId)
    {
        Assert.True(LocalMusicId.TryParse(id, out var k, out var e));
        Assert.Equal(kind, k);
        Assert.Equal(entityId, e);
    }

    [Theory]
    [InlineData("local:track:")]      // empty entity id
    [InlineData("local:album:abc")]   // unknown kind
    [InlineData("spotify:track:abc")] // not local
    [InlineData("garbage")]
    public void TryParse_rejects_bad_ids(string id) =>
        Assert.False(LocalMusicId.TryParse(id, out _, out _));
}
