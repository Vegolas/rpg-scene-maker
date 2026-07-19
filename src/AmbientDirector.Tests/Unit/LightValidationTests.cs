using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

public class LightValidationTests
{
    [Theory]
    [InlineData("lamp")]
    [InlineData("back-left_2")]
    [InlineData("ABC")]
    public void IsSlug_accepts_slug_chars(string s) => Assert.True(LightValidation.IsSlug(s));

    [Theory]
    [InlineData("has space")]
    [InlineData("dot.name")]
    [InlineData("slash/")]
    public void IsSlug_rejects_other_chars(string s) => Assert.False(LightValidation.IsSlug(s));

    [Theory]
    [InlineData("#abc", "#AABBCC")]       // 3-digit expands and upper-cases
    [InlineData("abc", "#AABBCC")]        // leading # optional
    [InlineData("ff8c2a", "#FF8C2A")]     // 6-digit upper-cased
    [InlineData("  #Ff8C2a  ", "#FF8C2A")] // trimmed
    public void NormalizeHex_expands_and_uppercases(string raw, string expected) =>
        Assert.Equal(expected, LightValidation.NormalizeHex(raw));

    [Theory]
    [InlineData("#12")]      // too short
    [InlineData("#12345")]   // 5 digits
    [InlineData("#GGG")]     // non-hex triple
    [InlineData("nothex")]
    public void NormalizeHex_throws_on_invalid(string raw) =>
        Assert.ThrowsAny<ArgumentException>(() => LightValidation.NormalizeHex(raw));
}

public class LightConfigValidationTests
{
    private static RegisteredLightDto Tuya(string key) => new(key, key, "tuya", null);

    [Fact]
    public void Null_list_is_accepted() => LightConfigValidation.Validate(null);

    [Fact]
    public void Accepts_distinct_slug_keys() =>
        LightConfigValidation.Validate([Tuya("a"), Tuya("b")]);

    [Fact]
    public void Rejects_non_slug_key() =>
        Assert.ThrowsAny<ArgumentException>(() => LightConfigValidation.Validate([Tuya("bad key")]));

    [Fact]
    public void Rejects_case_insensitive_duplicate_keys() =>
        Assert.ThrowsAny<ArgumentException>(() => LightConfigValidation.Validate([Tuya("Lamp"), Tuya("lamp")]));

    [Theory]
    [InlineData("zigbee")]
    [InlineData("")]
    public void Rejects_provider_outside_whitelist(string provider) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            LightConfigValidation.Validate([new RegisteredLightDto("k", "k", provider, null)]));

    [Fact]
    public void Hue_light_requires_a_hue_id() =>
        Assert.ThrowsAny<ArgumentException>(() =>
            LightConfigValidation.Validate([new RegisteredLightDto("k", "k", "hue", null)]));

    [Fact]
    public void Hue_light_with_hue_id_is_accepted() =>
        LightConfigValidation.Validate([new RegisteredLightDto("k", "k", "hue", "7")]);

    [Fact]
    public void ValidateDefault_null_returns_null() =>
        Assert.Null(LightConfigValidation.ValidateDefault(null));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(101, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 101)]
    public void ValidateDefault_rejects_out_of_range(int brightness, int temperature) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            LightConfigValidation.ValidateDefault(new DefaultLightDto(true, null, brightness, temperature)));

    [Fact]
    public void ValidateDefault_returns_copy_with_normalised_colour()
    {
        var result = LightConfigValidation.ValidateDefault(new DefaultLightDto(true, "#abc", 50, 50));
        Assert.Equal("#AABBCC", result!.Color);
        Assert.Equal(50, result.Brightness);
    }
}
