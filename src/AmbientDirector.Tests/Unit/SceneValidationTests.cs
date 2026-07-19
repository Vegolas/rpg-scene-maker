using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

public class SceneValidationTests
{
    private static Scene Valid() => new() { Id = "tavern", Name = "Tavern" };

    [Fact]
    public void Accepts_a_minimal_scene() => SceneValidation.Validate(Valid());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("bang!")]
    [InlineData("slash/")]
    public void Rejects_bad_ids(string id)
    {
        var scene = Valid();
        scene.Id = id;
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Theory]
    [InlineData("tavern")]
    [InlineData("Tavern-1_2")]
    [InlineData("ABC123")]
    public void Accepts_slug_ids(string id)
    {
        var scene = Valid();
        scene.Id = id;
        SceneValidation.Validate(scene);
    }

    [Fact]
    public void Rejects_missing_name()
    {
        var scene = Valid();
        scene.Name = "  ";
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_light_brightness_out_of_range(int brightness)
    {
        var scene = Valid();
        scene.Light = new LightSettings { Brightness = brightness };
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_light_temperature_out_of_range(int temperature)
    {
        var scene = Valid();
        scene.Light = new LightSettings { Temperature = temperature };
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Fact]
    public void Normalises_legacy_light_colour_in_place()
    {
        var scene = Valid();
        scene.Light = new LightSettings { Color = "#abc" };
        SceneValidation.Validate(scene);
        Assert.Equal("#AABBCC", scene.Light.Color);
    }

    [Fact]
    public void Coalesces_null_collections_to_empty()
    {
        var scene = Valid();
        scene.Lights = null!;
        scene.SoundEffects = null!;
        SceneValidation.Validate(scene);
        Assert.NotNull(scene.Lights);
        Assert.Empty(scene.Lights);
        Assert.NotNull(scene.SoundEffects);
        Assert.Empty(scene.SoundEffects);
    }

    [Fact]
    public void Rejects_per_light_entry_without_slug_key()
    {
        var scene = Valid();
        scene.Lights = [new SceneLight { LightKey = "not a slug" }];
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Fact]
    public void Normalises_per_light_colour_in_place()
    {
        var scene = Valid();
        scene.Lights = [new SceneLight { LightKey = "lamp", Color = "#abc" }];
        SceneValidation.Validate(scene);
        Assert.Equal("#AABBCC", scene.Lights[0].Color);
    }

    [Fact]
    public void Rejects_unknown_effect_type()
    {
        var scene = Valid();
        scene.Lights = [new SceneLight { LightKey = "lamp", Effect = new LightEffect { Type = "sparkle" } }];
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Rejects_effect_speed_out_of_range(int speed)
    {
        var scene = Valid();
        scene.Lights = [new SceneLight { LightKey = "lamp", Effect = new LightEffect { Type = "glow", Speed = speed, Intensity = 5 } }];
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Rejects_effect_intensity_out_of_range(int intensity)
    {
        var scene = Valid();
        scene.Lights = [new SceneLight { LightKey = "lamp", Effect = new LightEffect { Type = "glow", Speed = 5, Intensity = intensity } }];
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Fact]
    public void Drift_effect_requires_at_least_two_colours()
    {
        var scene = Valid();
        scene.Lights = [new SceneLight
        {
            LightKey = "lamp",
            Effect = new LightEffect { Type = "drift", Speed = 5, Intensity = 5, Colors = ["#ff0000"] },
        }];
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Fact]
    public void Drift_with_two_colours_passes_and_normalises_them()
    {
        var scene = Valid();
        scene.Lights = [new SceneLight
        {
            LightKey = "lamp",
            Effect = new LightEffect { Type = "drift", Speed = 5, Intensity = 5, Colors = ["#abc", "#def"] },
        }];
        SceneValidation.Validate(scene);
        Assert.Equal(["#AABBCC", "#DDEEFF"], scene.Lights[0].Effect!.Colors);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Rejects_music_volume_out_of_range(double volume)
    {
        var scene = Valid();
        scene.Music = new MusicSettings { Volume = volume };
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Fact]
    public void Rejects_non_spotify_play_id()
    {
        var scene = Valid();
        scene.Music = new MusicSettings { PlayId = "http://kenku/soundboard" };
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }

    [Fact]
    public void Accepts_spotify_play_id()
    {
        var scene = Valid();
        scene.Music = new MusicSettings { PlayId = "spotify:playlist:abc", Volume = 0.5 };
        SceneValidation.Validate(scene);
    }

    [Fact]
    public void Accepts_local_play_id()
    {
        var scene = Valid();
        scene.Music = new MusicSettings { Source = "local", PlayId = "local:track:tavern", Volume = 0.5 };
        SceneValidation.Validate(scene);
    }

    [Fact]
    public void Rejects_unknown_music_source()
    {
        var scene = Valid();
        scene.Music = new MusicSettings { Source = "youtube" };
        Assert.ThrowsAny<ArgumentException>(() => SceneValidation.Validate(scene));
    }
}
