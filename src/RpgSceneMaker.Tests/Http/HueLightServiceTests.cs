using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Tests.Store;
using Xunit;

namespace RpgSceneMaker.Tests.Http;

public class HueLightServiceTests
{
    private const string BridgeIp = "192.168.1.10";
    private const string AppKey = "appkey123";

    private static SettingsStore ConfiguredHue(SqliteTestDb db)
    {
        var settings = new SettingsStore(db);
        settings.Save(new LightingConfigDto(
            "hue",
            new HueConfigDto(BridgeIp, AppKey, []),   // empty light list -> group 0
            new TuyaConfigDto("", "", "", "3.3", "v2")));
        return settings;
    }

    private static (HueLightService svc, FakeHttpMessageHandler handler) Build(SettingsStore settings)
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler);
        return (new HueLightService(http, settings), handler);
    }

    private static JsonElement Body(FakeHttpMessageHandler handler) =>
        JsonDocument.Parse(handler.Requests[0].Body).RootElement;

    [Fact]
    public async Task SetColour_scales_hue_sat_and_bri_and_targets_group0()
    {
        using var db = new SqliteTestDb();
        var (svc, handler) = Build(ConfiguredHue(db));
        handler.Enqueue(HttpStatusCode.OK, "[{\"success\":{}}]");

        // green #00FF00 -> h=120: hue = round(120/360*65535)=21845, sat=254, bri=254
        await svc.SetColorAsync("#00FF00");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.Equal($"http://{BridgeIp}/api/{AppKey}/groups/0/action", req.Uri.ToString());

        var body = Body(handler);
        Assert.True(body.GetProperty("on").GetBoolean());
        Assert.Equal(21845, body.GetProperty("hue").GetInt32());
        Assert.Equal(254, body.GetProperty("sat").GetInt32());
        Assert.Equal(254, body.GetProperty("bri").GetInt32());
    }

    [Fact]
    public async Task SetColour_brightness_override_sets_bri_from_percent()
    {
        using var db = new SqliteTestDb();
        var (svc, handler) = Build(ConfiguredHue(db));
        handler.Enqueue(HttpStatusCode.OK, "[{\"success\":{}}]");

        // brightness 50 -> bri = round(50*254/100) = 127
        await svc.SetColorAsync("#00FF00", 50);

        Assert.Equal(127, Body(handler).GetProperty("bri").GetInt32());
    }

    [Fact]
    public async Task SetWhite_without_temperature_resets_saturation()
    {
        using var db = new SqliteTestDb();
        var (svc, handler) = Build(ConfiguredHue(db));
        handler.Enqueue(HttpStatusCode.OK, "[{\"success\":{}}]");

        // bri = round(80*254/100) = 203
        await svc.SetWhiteAsync(80);

        var body = Body(handler);
        Assert.True(body.GetProperty("on").GetBoolean());
        Assert.Equal(203, body.GetProperty("bri").GetInt32());
        Assert.Equal(0, body.GetProperty("sat").GetInt32());
        Assert.False(body.TryGetProperty("ct", out _));
    }

    [Fact]
    public async Task SetWhite_with_temperature_sets_ct_in_mireds_and_drops_sat()
    {
        using var db = new SqliteTestDb();
        var (svc, handler) = Build(ConfiguredHue(db));
        handler.Enqueue(HttpStatusCode.OK, "[{\"success\":{}}]");

        // temp 40 -> ct = 500 - round(40*(500-153)/100) = 500 - 139 = 361; bri = round(40*254/100) = 102
        await svc.SetWhiteAsync(40, 40);

        var body = Body(handler);
        Assert.Equal(102, body.GetProperty("bri").GetInt32());
        Assert.Equal(361, body.GetProperty("ct").GetInt32());
        Assert.False(body.TryGetProperty("sat", out _));  // ct implies white; sat removed
    }

    [Fact]
    public async Task ToBri_clamps_zero_percent_up_to_one()
    {
        using var db = new SqliteTestDb();
        var (svc, handler) = Build(ConfiguredHue(db));
        handler.Enqueue(HttpStatusCode.OK, "[{\"success\":{}}]");

        await svc.SetWhiteAsync(0);   // bri floor is 1, never 0

        Assert.Equal(1, Body(handler).GetProperty("bri").GetInt32());
    }

    [Fact]
    public async Task Bridge_200_with_error_body_throws_HueException()
    {
        using var db = new SqliteTestDb();
        var (svc, handler) = Build(ConfiguredHue(db));
        // The bridge answers HTTP 200 even on failures; the error is in the body.
        handler.Enqueue(HttpStatusCode.OK, "[{\"error\":{\"type\":101,\"description\":\"link button not pressed\"}}]");

        var ex = await Assert.ThrowsAsync<HueException>(() => svc.SetColorAsync("#00FF00"));
        Assert.Contains("link button not pressed", ex.Message);
    }

    [Fact]
    public async Task Unconfigured_bridge_throws_InvalidOperationException()
    {
        using var db = new SqliteTestDb();
        var settings = new SettingsStore(db);   // nothing saved -> empty Hue config
        var (svc, _) = Build(settings);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => svc.SetColorAsync("#00FF00"));
    }
}
