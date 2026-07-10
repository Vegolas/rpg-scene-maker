using Microsoft.AspNetCore.Http;
using RpgSceneMaker.Api.Endpoints;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class SetupEndpointsInternalsTests
{
    [Fact]
    public void Base64Url_strips_padding()
    {
        // Base64 of a single zero byte is "AA==" — url-safe form drops the padding.
        Assert.Equal("AA", SetupEndpoints.Base64Url([0x00]));
    }

    [Fact]
    public void Base64Url_uses_url_safe_alphabet()
    {
        // 0xFF,0xFF -> base64 "//8=" -> url-safe "__8" ('/' -> '_', padding gone).
        Assert.Equal("__8", SetupEndpoints.Base64Url([0xFF, 0xFF]));
        // 0xFB,0xFF,0xFE -> base64 "+//+" -> url-safe "-__-" ('+' -> '-', '/' -> '_').
        Assert.Equal("-__-", SetupEndpoints.Base64Url([0xFB, 0xFF, 0xFE]));
    }

    private static HttpRequest Request(string scheme, string host, int? port)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = port is { } p ? new HostString(host, p) : new HostString(host);
        return ctx.Request;
    }

    [Fact]
    public void SpotifyRedirectUri_rewrites_localhost_to_loopback_ip_keeping_port()
    {
        var uri = SetupEndpoints.SpotifyRedirectUri(Request("http", "localhost", 5252));
        Assert.Equal("http://127.0.0.1:5252/setup/spotify/callback", uri);
    }

    [Fact]
    public void SpotifyRedirectUri_rewrites_localhost_without_port()
    {
        var uri = SetupEndpoints.SpotifyRedirectUri(Request("http", "localhost", null));
        Assert.Equal("http://127.0.0.1/setup/spotify/callback", uri);
    }

    [Fact]
    public void SpotifyRedirectUri_rewrites_ipv6_loopback()
    {
        var uri = SetupEndpoints.SpotifyRedirectUri(Request("http", "[::1]", 5252));
        Assert.Equal("http://127.0.0.1:5252/setup/spotify/callback", uri);
    }

    [Fact]
    public void SpotifyRedirectUri_leaves_other_hosts_alone()
    {
        var uri = SetupEndpoints.SpotifyRedirectUri(Request("http", "192.168.1.50", 5252));
        Assert.Equal("http://192.168.1.50:5252/setup/spotify/callback", uri);
    }
}
