using RpgSceneMaker.Api.Services;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class ColorMathTests
{
    [Theory]
    [InlineData("#FF8C2A", 255, 140, 42)]
    [InlineData("FF8C2A", 255, 140, 42)]   // leading # optional
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    public void ParseHexColor_reads_rrggbb(string hex, int r, int g, int b)
    {
        Assert.Equal((r, g, b), ColorMath.ParseHexColor(hex));
    }

    [Theory]
    [InlineData("#FFF")]        // 3 digits not accepted by ColorMath (that's LightValidation's job)
    [InlineData("#12345")]      // 5 digits
    [InlineData("#1234567")]    // 7 digits
    [InlineData("#GGGGGG")]     // non-hex
    [InlineData("")]
    public void ParseHexColor_rejects_non_6_hex(string hex)
    {
        Assert.Throws<ArgumentException>(() => ColorMath.ParseHexColor(hex));
    }

    [Theory]
    [InlineData(255, 0, 0, 0.0)]     // pure red
    [InlineData(0, 255, 0, 120.0)]   // pure green
    [InlineData(0, 0, 255, 240.0)]   // pure blue
    public void RgbToHsv_primaries_have_expected_hue_full_sat_full_value(int r, int g, int b, double hue)
    {
        var (h, s, v) = ColorMath.RgbToHsv(r, g, b);
        Assert.Equal(hue, h, 3);
        Assert.Equal(1.0, s, 6);
        Assert.Equal(1.0, v, 6);
    }

    [Fact]
    public void RgbToHsv_white_is_zero_saturation_full_value()
    {
        var (h, s, v) = ColorMath.RgbToHsv(255, 255, 255);
        Assert.Equal(0.0, h);
        Assert.Equal(0.0, s);   // delta == 0 branch
        Assert.Equal(1.0, v);
    }

    [Fact]
    public void RgbToHsv_black_is_zero_value_and_saturation()
    {
        var (h, s, v) = ColorMath.RgbToHsv(0, 0, 0);
        Assert.Equal(0.0, h);
        Assert.Equal(0.0, s);   // max == 0 guard
        Assert.Equal(0.0, v);
    }

    [Fact]
    public void RgbToHsv_mid_grey_has_zero_saturation()
    {
        var (_, s, v) = ColorMath.RgbToHsv(128, 128, 128);
        Assert.Equal(0.0, s);           // delta == 0
        Assert.Equal(128 / 255.0, v, 6);
    }

    [Fact]
    public void RgbToHsv_wraps_negative_hue_into_0_360()
    {
        // Red-dominant with slightly more blue than green drives the raw hue negative → wraps near 360.
        var (h, _, _) = ColorMath.RgbToHsv(255, 0, 1);
        Assert.InRange(h, 359.0, 360.0);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]      // red
    [InlineData(120, 0, 255, 0)]    // green
    [InlineData(240, 0, 0, 255)]    // blue
    public void HsvToRgb_primaries(double h, int r, int g, int b)
    {
        Assert.Equal((r, g, b), ColorMath.HsvToRgb(h, 1.0, 1.0));
    }

    [Fact]
    public void HsvToRgb_zero_saturation_is_grey()
    {
        Assert.Equal((128, 128, 128), ColorMath.HsvToRgb(210, 0.0, 128 / 255.0));
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 128, 64)]
    [InlineData(10, 20, 30)]
    [InlineData(200, 200, 50)]
    [InlineData(17, 34, 51)]
    [InlineData(240, 248, 255)]
    public void RgbToHsv_then_HsvToRgb_round_trips(int r, int g, int b)
    {
        var (h, s, v) = ColorMath.RgbToHsv(r, g, b);
        var (r2, g2, b2) = ColorMath.HsvToRgb(h, s, v);
        // Allow a 1-unit rounding wobble per channel.
        Assert.InRange(r2, r - 1, r + 1);
        Assert.InRange(g2, g - 1, g + 1);
        Assert.InRange(b2, b - 1, b + 1);
    }
}
