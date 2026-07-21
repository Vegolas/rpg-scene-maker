using AmbientDirector.Ui.Contracts;
using AmbientDirector.Ui.Shared;
using Xunit;

namespace AmbientDirector.Tests.Unit;

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

public class PartyRenderTests
{
    // Stand-in for Api.ImageUrl: prefixes a name so the test can see a portrait was routed through it (the real
    // one attaches the key-bearing /images path); null stays null.
    private static string? Img(string? name) => name is null ? null : $"/images/{name}?apiKey=k";

    [Fact]
    public void ToRenderModel_maps_players_counters_and_routes_portraits_through_imageUrl()
    {
        var party = new PartyDto(
            [
                new PartyPlayerDto("kira", "Kira", "kira.png", 0,
                    [new PartyCounterDto("HP", 7, 10, "number"), new PartyCounterDto("Hope", 3, 6, "pips")]),
                new PartyPlayerDto("aldous", "Aldous", null, 1, null),
            ],
            [new PartyCounterDto("Fear", 4, 12, "pips")],
            [
                new PartyEnemyDto("goblin", "Goblin", "goblin.png", 0, [new PartyCounterDto("HP", 3, 4, "pips")]),
                new PartyEnemyDto("boss", "Dread King", null, 1, null),
            ],
            "daggerheart");

        var model = PartyRender.ToRenderModel(party, Img);

        Assert.Equal(2, model.Players.Count);
        var kira = model.Players[0];
        Assert.Equal("Kira", kira.Name);
        Assert.Equal("/images/kira.png?apiKey=k", kira.PortraitUrl); // routed through imageUrl
        Assert.Equal(2, kira.Counters.Count);
        Assert.Equal("HP", kira.Counters[0].Label);
        Assert.Equal(7, kira.Counters[0].Value);
        Assert.Equal(10, kira.Counters[0].Max);
        Assert.Equal("number", kira.Counters[0].Style);

        // A null portrait stays null; a null counters list degrades to empty (never a null-deref at render).
        Assert.Null(model.Players[1].PortraitUrl);
        Assert.Empty(model.Players[1].Counters);

        // Table-level counters carried through unchanged.
        Assert.Single(model.Counters);
        Assert.Equal("Fear", model.Counters[0].Label);
        Assert.Equal(4, model.Counters[0].Value);

        // Enemies (bestiary templates) map through: name + portrait routed through imageUrl + counters; the
        // per-instance spotlight is never a template's, so it's always false here. A null counters list → empty.
        Assert.Equal(2, model.Enemies.Count);
        Assert.Equal("Goblin", model.Enemies[0].Name);
        Assert.Equal("/images/goblin.png?apiKey=k", model.Enemies[0].PortraitUrl); // routed through imageUrl
        Assert.False(model.Enemies[0].Spotlight);
        Assert.Equal("HP", model.Enemies[0].Counters[0].Label);
        Assert.Equal(3, model.Enemies[0].Counters[0].Value);
        Assert.Null(model.Enemies[1].PortraitUrl);
        Assert.False(model.Enemies[1].Spotlight);
        Assert.Empty(model.Enemies[1].Counters);
    }
}

public class CounterEditTests
{
    // A null wire style (renderer decides) surfaces as an explicit Segmented value the editor can bind.
    [Theory]
    [InlineData(null, 6, "pips")]    // small max → pips
    [InlineData(null, null, "number")] // unbounded → number
    [InlineData(null, 30, "number")] // too-large max for pips → number
    [InlineData("number", 6, "number")] // an explicit style is preserved
    public void FromDto_resolves_an_explicit_style(string? wireStyle, int? max, string expected)
    {
        var edit = CounterEdit.FromDto(new PartyCounterDto("HP", 3, max, wireStyle));
        Assert.Equal(expected, edit.Style);
    }

    [Fact]
    public void ToDto_trims_the_label_and_writes_the_explicit_style()
    {
        var dto = new CounterEdit { Label = "  HP  ", Value = 3, Max = 6, Style = "pips" }.ToDto();
        Assert.Equal("HP", dto.Label);
        Assert.Equal("pips", dto.Style);
        Assert.Equal(6, dto.Max);
    }
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
