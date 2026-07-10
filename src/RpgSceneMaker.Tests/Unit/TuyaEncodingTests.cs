using Microsoft.Extensions.Logging.Abstractions;
using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Tests.Store;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

// The Tuya bulb has no HTTP seam (it speaks raw TCP), so its pure DP-packing helpers are tested
// directly against hand-computed hex payloads. The service reads its profile from SettingsStore.
public class TuyaEncodingTests
{
    private static TuyaLightService Service(SqliteTestDb db, string dpProfile)
    {
        var settings = new SettingsStore(db);
        settings.Save(new LightingConfigDto(
            "tuya",
            new HueConfigDto("", "", []),
            new TuyaConfigDto("1.2.3.4", "dev", "key", "3.3", dpProfile)));
        return new TuyaLightService(settings, NullLogger<TuyaLightService>.Instance);
    }

    [Fact]
    public void V2_encode_colour_packs_hue4_sat4_val4()
    {
        using var db = new SqliteTestDb();
        var svc = Service(db, "v2");
        // red: hue 0, s/v 1000 (0x3e8)
        Assert.Equal("000003e803e8", svc.EncodeColour(0, 1.0, 1.0));
        // hue 240 (0x00f0), s 0.5 -> 500 (0x01f4), v 0.8 -> 800 (0x0320)
        Assert.Equal("00f001f40320", svc.EncodeColour(240, 0.5, 0.8));
    }

    [Fact]
    public void V1_encode_colour_packs_rgb_hue4_sat2_val2()
    {
        using var db = new SqliteTestDb();
        var svc = Service(db, "v1");
        // red -> rgb ff0000, hue 0000, s 255 (ff), v 255 (ff)
        Assert.Equal("ff00000000ffff", svc.EncodeColour(0, 1.0, 1.0));
    }

    [Fact]
    public void V2_decode_colour_round_trips_the_encoding()
    {
        using var db = new SqliteTestDb();
        var svc = Service(db, "v2");
        var (h, s, v) = svc.DecodeColour("00f001f40320");
        Assert.Equal(240, h, 3);
        Assert.Equal(0.5, s, 3);
        Assert.Equal(0.8, v, 3);
    }

    [Fact]
    public void Decode_colour_falls_back_to_white_on_garbage()
    {
        using var db = new SqliteTestDb();
        var svc = Service(db, "v2");
        Assert.Equal((0.0, 0.0, 1.0), svc.DecodeColour("short"));
    }

    [Theory]
    [InlineData(1, 20)]     // 10 + round(990 * 0.01) = 10 + 10
    [InlineData(50, 505)]   // 10 + round(990 * 0.5) = 10 + 495
    [InlineData(100, 1000)] // 10 + 990
    [InlineData(0, 20)]     // clamped up to 1 -> same as 1
    public void V2_scale_brightness(int percent, int expected)
    {
        using var db = new SqliteTestDb();
        Assert.Equal(expected, Service(db, "v2").ScaleBrightness(percent));
    }

    [Theory]
    [InlineData(1, 27)]     // 25 + round(230 * 0.01) = 25 + 2
    [InlineData(50, 140)]   // 25 + round(230 * 0.5) = 25 + 115
    [InlineData(100, 255)]  // 25 + 230
    public void V1_scale_brightness(int percent, int expected)
    {
        using var db = new SqliteTestDb();
        Assert.Equal(expected, Service(db, "v1").ScaleBrightness(percent));
    }
}
