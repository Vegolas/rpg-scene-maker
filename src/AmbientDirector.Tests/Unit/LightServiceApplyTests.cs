using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using Xunit;

namespace AmbientDirector.Tests.Unit;

public class LightServiceApplyTests
{
    // Records which mode-setting call the default ApplyAsync routed to.
    private sealed class RecordingLight : ILightService
    {
        public List<string> Calls { get; } = [];

        public Task SetPowerAsync(bool on, string? targetId = null, int? transitionMs = null)
        {
            Calls.Add($"power:{on}");
            return Task.CompletedTask;
        }

        public Task SetColorAsync(string hexColor, int? brightnessPercent = null, string? targetId = null, int? transitionMs = null)
        {
            Calls.Add($"color:{hexColor}:{brightnessPercent}");
            return Task.CompletedTask;
        }

        public Task SetWhiteAsync(int brightnessPercent, int? temperaturePercent = null, string? targetId = null, int? transitionMs = null)
        {
            Calls.Add($"white:{brightnessPercent}:{temperaturePercent}");
            return Task.CompletedTask;
        }

        public Task<bool> ToggleAsync() => throw new NotSupportedException();
        public Task SetBrightnessAsync(int percent, string? targetId = null, int? transitionMs = null) => throw new NotSupportedException();
        public Task<LightStatus> GetStatusAsync() => throw new NotSupportedException();
    }

    private static async Task<List<string>> ApplyAsync(LightSettings light)
    {
        var recorder = new RecordingLight();
        await ((ILightService)recorder).ApplyAsync(light);
        return recorder.Calls;
    }

    [Fact]
    public async Task Power_off_only_turns_off()
    {
        // Even with a colour set, power:false short-circuits everything else.
        var calls = await ApplyAsync(new LightSettings { Power = false, Color = "#FF0000", Brightness = 80 });
        Assert.Equal(["power:False"], calls);
    }

    [Fact]
    public async Task Colour_set_uses_colour_and_never_white()
    {
        var calls = await ApplyAsync(new LightSettings { Color = "#FF0000", Brightness = 80, Temperature = 30 });
        Assert.Equal(["color:#FF0000:80"], calls);
    }

    [Fact]
    public async Task Brightness_or_temperature_only_uses_white()
    {
        var calls = await ApplyAsync(new LightSettings { Brightness = 60, Temperature = 40 });
        Assert.Equal(["white:60:40"], calls);
    }

    [Fact]
    public async Task Temperature_only_defaults_brightness_to_100()
    {
        var calls = await ApplyAsync(new LightSettings { Temperature = 40 });
        Assert.Equal(["white:100:40"], calls);
    }

    [Fact]
    public async Task Power_true_alone_just_turns_on()
    {
        var calls = await ApplyAsync(new LightSettings { Power = true });
        Assert.Equal(["power:True"], calls);
    }

    [Fact]
    public async Task Empty_settings_do_nothing()
    {
        var calls = await ApplyAsync(new LightSettings());
        Assert.Empty(calls);
    }
}
