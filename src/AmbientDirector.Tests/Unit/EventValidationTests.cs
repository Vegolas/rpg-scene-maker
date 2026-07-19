using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

public class EventValidationTests
{
    private static GameEvent Valid() => new() { Id = "thunder", Name = "Thunder" };

    [Fact]
    public void Accepts_a_minimal_event() => EventValidation.Validate(Valid());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("bang!")]
    public void Rejects_bad_ids(string id)
    {
        var evt = Valid();
        evt.Id = id;
        Assert.ThrowsAny<ArgumentException>(() => EventValidation.Validate(evt));
    }

    [Fact]
    public void Rejects_missing_name()
    {
        var evt = Valid();
        evt.Name = "  ";
        Assert.ThrowsAny<ArgumentException>(() => EventValidation.Validate(evt));
    }

    [Fact]
    public void Normalises_flash_colour_in_place()
    {
        var evt = Valid();
        evt.Flash = new EventFlash { Color = "#fff", Brightness = 100, DurationMs = 200 };
        EventValidation.Validate(evt);
        Assert.Equal("#FFFFFF", evt.Flash.Color);
    }

    [Fact]
    public void Rejects_invalid_flash_colour()
    {
        var evt = Valid();
        evt.Flash = new EventFlash { Color = "not-a-colour", Brightness = 100, DurationMs = 200 };
        Assert.ThrowsAny<ArgumentException>(() => EventValidation.Validate(evt));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_flash_brightness_out_of_range(int brightness)
    {
        var evt = Valid();
        evt.Flash = new EventFlash { Color = "#FFFFFF", Brightness = brightness, DurationMs = 200 };
        Assert.ThrowsAny<ArgumentException>(() => EventValidation.Validate(evt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public void Rejects_flash_duration_out_of_range(int durationMs)
    {
        var evt = Valid();
        evt.Flash = new EventFlash { Color = "#FFFFFF", Brightness = 100, DurationMs = durationMs };
        Assert.ThrowsAny<ArgumentException>(() => EventValidation.Validate(evt));
    }

    [Fact]
    public void Coalesces_null_sound_effects_to_empty()
    {
        var evt = Valid();
        evt.SoundEffects = null!;
        EventValidation.Validate(evt);
        Assert.NotNull(evt.SoundEffects);
        Assert.Empty(evt.SoundEffects);
    }
}
