using RpgSceneMaker.Api.Services;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class SpotifyAuthStateTests
{
    [Fact]
    public void Take_returns_the_entry_once_then_null()
    {
        var auth = new SpotifyAuthState();
        auth.Add("state1", "verifier1", "https://host/callback");

        var first = auth.Take("state1");
        Assert.NotNull(first);
        Assert.Equal("verifier1", first!.Verifier);
        Assert.Equal("https://host/callback", first.RedirectUri);

        // Single use: a second take of the same state finds nothing.
        Assert.Null(auth.Take("state1"));
    }

    [Fact]
    public void Take_unknown_state_returns_null()
    {
        var auth = new SpotifyAuthState();
        Assert.Null(auth.Take("never-added"));
    }
}
