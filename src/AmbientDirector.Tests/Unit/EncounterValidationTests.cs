using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>Guards the encounter validator (issue #122): id/name rules, hero-id slugs, per-instance checks
/// (instance id present + unique, enemy ref, name, portrait, shared counter guard), background image, and the
/// optional scene/event refs (normalized empty → null, else a slug). The endpoint round-trip is covered by
/// EncounterTests; this pins the code paths directly.</summary>
public class EncounterValidationTests
{
    private static Encounter Valid() => new()
    {
        Id = "goblin-ambush",
        Name = "Goblin Ambush",
        HeroIds = ["kira"],
        Enemies =
        [
            new EncounterEnemy
            {
                InstanceId = "goblin-1",
                EnemyId = "goblin",
                Name = "Goblin 1",
                Counters = [new PartyCounter { Label = "HP", Value = 6, Max = 6, Style = "number" }],
            },
        ],
    };

    [Fact]
    public void Accepts_a_valid_encounter_and_defaults_null_lists()
    {
        var e = Valid();
        e.HeroIds = null!;
        e.Enemies = null!;
        EncounterValidation.Validate(e); // must not throw
        Assert.NotNull(e.HeroIds);
        Assert.NotNull(e.Enemies);
    }

    [Fact]
    public void Rejects_missing_id_name_and_bad_slug()
    {
        AssertCode(e => e.Id = "", "error.common.idRequired");
        AssertCode(e => e.Id = "bad name", "error.common.idSlug");
        AssertCode(e => e.Name = "  ", "error.common.nameRequired");
    }

    [Fact]
    public void Rejects_a_non_slug_hero_id_reporting_position()
    {
        var e = Valid();
        e.HeroIds = ["kira", "bad id"];
        var ex = Assert.ThrowsAny<ValidationException>(() => EncounterValidation.Validate(e));
        Assert.Equal("error.encounter.heroId", ex.Code);
        Assert.Equal(2, ex.Args[0]); // 1-based position
    }

    [Fact]
    public void Trims_hero_ids_in_place()
    {
        var e = Valid();
        e.HeroIds = ["  kira  "];
        EncounterValidation.Validate(e);
        Assert.Equal("kira", e.HeroIds[0]);
    }

    [Fact]
    public void Rejects_instance_problems_with_position()
    {
        AssertCode(e => e.Enemies[0].InstanceId = "", "error.encounter.instanceId");
        AssertCode(e => e.Enemies[0].EnemyId = "", "error.encounter.enemyId");
        AssertCode(e => e.Enemies[0].Name = "", "error.encounter.enemyName");
        AssertCode(e => e.Enemies[0].Portrait = "../hack.png", "error.common.invalidImage");
    }

    [Fact]
    public void Rejects_duplicate_instance_ids_case_insensitively()
    {
        var e = Valid();
        e.Enemies.Add(new EncounterEnemy { InstanceId = "GOBLIN-1", EnemyId = "goblin", Name = "Goblin 2" });
        var ex = Assert.ThrowsAny<ValidationException>(() => EncounterValidation.Validate(e));
        Assert.Equal("error.encounter.duplicateInstance", ex.Code);
        Assert.Equal(2, ex.Args[0]);
    }

    [Fact]
    public void Runs_the_shared_counter_guard_on_each_instance()
    {
        var e = Valid();
        e.Enemies[0].Counters = [new PartyCounter { Label = "HP", Value = 1, Style = "pips" }]; // no max
        var ex = Assert.ThrowsAny<ValidationException>(() => EncounterValidation.Validate(e));
        Assert.Equal("error.party.pipsMax", ex.Code);
    }

    [Fact]
    public void Rejects_too_many_instances()
    {
        var e = Valid();
        e.Enemies = [.. Enumerable.Range(0, 51).Select(i => new EncounterEnemy
        {
            InstanceId = $"g{i}", EnemyId = "goblin", Name = $"Goblin {i}",
        })];
        var ex = Assert.ThrowsAny<ValidationException>(() => EncounterValidation.Validate(e));
        Assert.Equal("error.encounter.tooManyEnemies", ex.Code);
    }

    [Fact]
    public void Normalizes_empty_scene_event_refs_to_null_and_validates_slugs()
    {
        var e = Valid();
        e.ActivateSceneId = "   ";
        e.ActivateEventId = "";
        EncounterValidation.Validate(e);
        Assert.Null(e.ActivateSceneId);
        Assert.Null(e.ActivateEventId);

        AssertCode(x => x.ActivateSceneId = "bad scene", "error.encounter.sceneId");
        AssertCode(x => x.ActivateEventId = "bad event", "error.encounter.eventId");
    }

    [Fact]
    public void Rejects_a_bad_background_image()
    {
        AssertCode(e => e.BackgroundImage = "../secret.png", "error.common.invalidImage");
    }

    private static void AssertCode(Action<Encounter> mutate, string expectedCode)
    {
        var e = Valid();
        mutate(e);
        var ex = Assert.ThrowsAny<ValidationException>(() => EncounterValidation.Validate(e));
        Assert.Equal(expectedCode, ex.Code);
    }
}
