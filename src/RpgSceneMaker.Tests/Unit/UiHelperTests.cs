using RpgSceneMaker.Ui.Contracts;
using RpgSceneMaker.Ui.Shared;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class SceneNamingTests
{
    [Fact]
    public void SplitName_splits_leading_emoji_from_label()
    {
        var (emoji, label) = SceneNaming.SplitName("🍺 Tavern");
        Assert.Equal("🍺", emoji);
        Assert.Equal("Tavern", label);
    }

    [Fact]
    public void SplitName_without_emoji_keeps_whole_name_as_label()
    {
        var (emoji, label) = SceneNaming.SplitName("Dark Forest", emojiFallback: "🎲");
        Assert.Equal("🎲", emoji);
        Assert.Equal("Dark Forest", label);
    }

    [Fact]
    public void SplitName_blank_name_uses_label_fallback()
    {
        var (emoji, label) = SceneNaming.SplitName("   ", emojiFallback: "", labelFallback: "scene-id");
        Assert.Equal("", emoji);
        Assert.Equal("scene-id", label);
    }

    [Theory]
    [InlineData("Tavern Brawl", "tavern-brawl")]
    [InlineData("  Multiple   Spaces  ", "multiple-spaces")]
    [InlineData("a__b--c", "a-b-c")]
    [InlineData("UPPER", "upper")]
    [InlineData("-leading-and-trailing-", "leading-and-trailing")]
    public void Slugify_lowercases_and_collapses_separators(string input, string expected) =>
        Assert.Equal(expected, SceneNaming.Slugify(input));

    [Fact]
    public void MakeUnique_suffixes_until_free()
    {
        Assert.Equal("lamp-2", SceneNaming.MakeUnique("lamp", ["lamp"]));
        Assert.Equal("lamp-3", SceneNaming.MakeUnique("lamp", ["lamp", "lamp-2"]));
        Assert.Equal("lamp", SceneNaming.MakeUnique("lamp", ["other"]));
    }

    [Fact]
    public void MakeUnique_is_case_insensitive()
    {
        Assert.Equal("Lamp-2", SceneNaming.MakeUnique("Lamp", ["lamp"]));
    }
}

public class LightFormatTests
{
    [Theory]
    [InlineData(0, "light.temp.warm")]
    [InlineData(33, "light.temp.warm")]
    [InlineData(34, "light.temp.neutral")]
    [InlineData(50, "light.temp.neutral")]
    [InlineData(66, "light.temp.neutral")]
    [InlineData(67, "light.temp.cold")]
    [InlineData(100, "light.temp.cold")]
    public void TempWordKey_boundaries(int temperature, string key) =>
        Assert.Equal(key, LightFormat.TempWordKey(temperature));
}

public class UiExtensionsTests
{
    private static ActivationDto Result(string light, string music, string sound) =>
        new("scene", light, music, sound, FullySucceeded: false);

    // Identity translator: returns the key verbatim, so assertions can see the part-label keys and the
    // extracted error-code tail (both of which the real Localizer would translate).
    private static string Tr(string key) => key;

    [Fact]
    public void ProblemSummary_joins_only_errored_parts_translating_label_and_code()
    {
        var summary = Result("ok", "error:error.title.spotify", "error:error.title.soundboard").ProblemSummary(Tr);
        Assert.Equal("error.part.music: error.title.spotify | error.part.sound: error.title.soundboard", summary);
    }

    [Fact]
    public void ProblemSummary_reports_the_light_part()
    {
        Assert.Equal("error.part.light: error.title.bulbUnreachable",
            Result("error:error.title.bulbUnreachable", "ok", "skipped").ProblemSummary(Tr));
    }

    [Fact]
    public void ProblemSummary_is_empty_when_nothing_errored()
    {
        Assert.Equal("", Result("ok", "skipped", "ok").ProblemSummary(Tr));
    }
}
