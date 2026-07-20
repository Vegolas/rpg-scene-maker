using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>Guards the enemy validator (issue #120): PartyValidation.Validate(Enemy) is the member validator's
/// twin — same id/name rules, and the counter limits are shared with the member/table-counter path unchanged.
/// The endpoint round-trip is covered by EnemyTests; this pins the id/name/counter code paths directly.</summary>
public class PartyValidationEnemyTests
{
    private static Enemy Valid() => new()
    {
        Id = "goblin",
        Name = "Goblin",
        Counters = [new PartyCounter { Label = "HP", Value = 3, Max = 4, Style = "pips" }],
    };

    [Fact]
    public void Accepts_a_valid_enemy_and_defaults_a_null_counter_list() // parity with the member validator
    {
        var enemy = Valid();
        enemy.Counters = null!; // JSON "counters": null must not throw — it overwrites the C# default
        PartyValidation.Validate(enemy); // must not throw
        Assert.NotNull(enemy.Counters);
    }

    [Fact]
    public void Rejects_a_missing_id()
    {
        var enemy = Valid();
        enemy.Id = "";
        var ex = Assert.ThrowsAny<ValidationException>(() => PartyValidation.Validate(enemy));
        Assert.Equal("error.common.idRequired", ex.Code);
    }

    [Fact]
    public void Rejects_a_non_slug_id()
    {
        var enemy = Valid();
        enemy.Id = "bad name"; // the space is not a slug char
        var ex = Assert.ThrowsAny<ValidationException>(() => PartyValidation.Validate(enemy));
        Assert.Equal("error.common.idSlug", ex.Code);
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        var enemy = Valid();
        enemy.Name = "   ";
        var ex = Assert.ThrowsAny<ValidationException>(() => PartyValidation.Validate(enemy));
        Assert.Equal("error.common.nameRequired", ex.Code);
    }

    [Fact]
    public void Runs_the_shared_counter_guard() // e.g. pips needs a bounded, TV-renderable max
    {
        var enemy = Valid();
        enemy.Counters = [new PartyCounter { Label = "HP", Value = 1, Style = "pips" }]; // no max
        var ex = Assert.ThrowsAny<ValidationException>(() => PartyValidation.Validate(enemy));
        Assert.Equal("error.party.pipsMax", ex.Code);
    }

    [Fact]
    public void Clamps_a_counter_value_into_range() // normalization, not an error (shared with members)
    {
        var enemy = Valid();
        enemy.Counters = [new PartyCounter { Label = "HP", Value = 999, Max = 4 }];
        PartyValidation.Validate(enemy);
        Assert.Equal(4, enemy.Counters[0].Value);
    }
}
