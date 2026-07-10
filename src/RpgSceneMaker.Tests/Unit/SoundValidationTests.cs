using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Validation;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class SoundValidationTests
{
    private static Sound Valid() => new() { Id = "thunder", Name = "Thunder", Category = "Weather", Volume = 0.5 };

    [Fact]
    public void Accepts_valid_sound() => SoundValidation.Validate(Valid());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_name(string name)
    {
        var s = Valid();
        s.Name = name;
        Assert.Throws<ArgumentException>(() => SoundValidation.Validate(s));
    }

    [Fact]
    public void Rejects_name_over_80_chars()
    {
        var s = Valid();
        s.Name = new string('x', 81);
        Assert.Throws<ArgumentException>(() => SoundValidation.Validate(s));
    }

    [Fact]
    public void Accepts_name_of_exactly_80_chars()
    {
        var s = Valid();
        s.Name = new string('x', 80);
        SoundValidation.Validate(s);
    }

    [Fact]
    public void Rejects_category_over_40_chars()
    {
        var s = Valid();
        s.Category = new string('x', 41);
        Assert.Throws<ArgumentException>(() => SoundValidation.Validate(s));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Rejects_volume_out_of_range(double volume)
    {
        var s = Valid();
        s.Volume = volume;
        Assert.Throws<ArgumentException>(() => SoundValidation.Validate(s));
    }
}
