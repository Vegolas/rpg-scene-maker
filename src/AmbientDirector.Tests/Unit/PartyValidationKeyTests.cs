using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>The counter's optional semantic key (issue #127): normalization, slug/length/uniqueness rules in
/// PartyValidation, and the key-first adjust-token resolution shared by every adjust path
/// (<see cref="PartyStore.FindCounter"/>). The endpoint round-trip is covered by SystemTests/PartyTests.</summary>
public class PartyValidationKeyTests
{
    private static PartyCounter Counter(string label, string? key = null, int max = 6) => new()
    {
        Label = label,
        Value = 1,
        Max = max,
        Style = "pips",
        Key = key,
    };

    [Fact]
    public void Keys_are_optional_and_normalized_to_trimmed_lowercase()
    {
        List<PartyCounter> counters = [Counter("HP", " HP "), Counter("Luck"), Counter("Mana", "  ")];

        PartyValidation.ValidateCounters(counters);

        Assert.Equal("hp", counters[0].Key);
        Assert.Null(counters[1].Key);
        Assert.Null(counters[2].Key); // whitespace off the wire means "no key"
    }

    [Theory]
    [InlineData("bad key")] // the space is not a slug char
    [InlineData("hp!")]
    public void Rejects_a_non_slug_key(string key)
    {
        var ex = Assert.ThrowsAny<ValidationException>(() =>
            PartyValidation.ValidateCounters([Counter("HP", key)]));
        Assert.Equal("error.party.counterKey", ex.Code);
    }

    [Fact]
    public void Rejects_an_overlong_key()
    {
        var ex = Assert.ThrowsAny<ValidationException>(() =>
            PartyValidation.ValidateCounters([Counter("HP", new string('k', 41))]));
        Assert.Equal("error.party.counterKey", ex.Code);
    }

    [Fact]
    public void Rejects_duplicate_keys_case_insensitively()
    {
        var ex = Assert.ThrowsAny<ValidationException>(() =>
            PartyValidation.ValidateCounters([Counter("HP", "hp"), Counter("Health", "HP")]));
        Assert.Equal("error.party.duplicateCounterKey", ex.Code);
    }

    [Fact]
    public void Adjust_token_resolves_key_first_then_label()
    {
        // A pathological-but-legal set: one counter's LABEL equals another counter's KEY. The key match must
        // win — keys are unique, so a token that is some counter's key never lands on another's label.
        List<PartyCounter> counters = [Counter("fear", key: null), Counter("Strach", "fear")];
        Assert.Same(counters[1], PartyStore.FindCounter(counters, "FEAR")); // key, case-insensitive

        // Without a key in play, the same token still falls back to the label match (Stream Deck back-compat).
        List<PartyCounter> keyless = [Counter("Fear")];
        Assert.Same(keyless[0], PartyStore.FindCounter(keyless, "fear"));

        Assert.Null(PartyStore.FindCounter(counters, "unknown"));
    }
}
