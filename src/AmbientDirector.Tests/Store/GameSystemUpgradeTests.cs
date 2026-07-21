using Microsoft.Extensions.Logging.Abstractions;
using AmbientDirector.Api.Data;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services.Systems;
using Xunit;

namespace AmbientDirector.Tests.Store;

/// <summary>The startup game-system one-shot (issue #127): installs with pre-existing game data get
/// "daggerheart" stamped and semantic counter keys backfilled by EN/PL label match — exactly once, and never
/// over an explicit "none". Mirrors <see cref="LegacyImporterTests"/> (the other startup one-shot).</summary>
public class GameSystemUpgradeTests
{
    private static PartyMember Member(params PartyCounter[] counters) => new()
    {
        Id = "kira",
        Name = "Kira",
        Counters = [.. counters],
    };

    private static PartyCounter Counter(string label, string? key = null) => new()
    {
        Label = label,
        Value = 1,
        Max = 6,
        Style = "pips",
        Key = key,
    };

    [Fact]
    public void Stamps_daggerheart_and_backfills_keys_across_every_counter_owner()
    {
        using var factory = new SqliteTestDb();
        using (var db = factory.CreateDbContext())
        {
            // Polish + English labels, a custom label, and a counter that already carries a (custom) key.
            db.PartyMembers.Add(Member(Counter("HP"), Counter("Nadzieja"), Counter("Luck")));
            db.Enemies.Add(new Enemy { Id = "goblin", Name = "Goblin", Counters = [Counter("Stres")] });
            db.Encounters.Add(new Encounter
            {
                Id = "ambush",
                Name = "Ambush",
                Enemies = [new EncounterEnemy
                {
                    InstanceId = "goblin-1",
                    EnemyId = "goblin",
                    Name = "Goblin 1",
                    Counters = [Counter("Pancerz"), Counter("HP", key: "custom")],
                }],
            });
            db.PartyConfigs.Add(new PartyConfig { Counters = [Counter("Strach")] });
            db.SaveChanges();

            GameSystemUpgrade.Run(db, NullLogger.Instance);
        }

        using (var db = factory.CreateDbContext())
        {
            Assert.Equal("daggerheart", db.PartyConfigs.Single().SystemId);

            var member = db.PartyMembers.Single().Counters;
            Assert.Equal("hp", member.Single(c => c.Label == "HP").Key);
            Assert.Equal("hope", member.Single(c => c.Label == "Nadzieja").Key);
            Assert.Null(member.Single(c => c.Label == "Luck").Key); // unknown label stays keyless

            Assert.Equal("stress", db.Enemies.Single().Counters.Single().Key);

            var instance = db.Encounters.Single().Enemies.Single().Counters;
            Assert.Equal("armor", instance.Single(c => c.Label == "Pancerz").Key);
            Assert.Equal("custom", instance.Single(c => c.Label == "HP").Key); // existing keys are kept

            Assert.Equal("fear", db.PartyConfigs.Single().Counters.Single().Key);
        }
    }

    [Fact]
    public void Fresh_install_without_game_data_stays_unchosen()
    {
        using var factory = new SqliteTestDb();
        using var db = factory.CreateDbContext();

        GameSystemUpgrade.Run(db, NullLogger.Instance);

        Assert.Null(db.PartyConfigs.SingleOrDefault()?.SystemId);
    }

    [Fact]
    public void Table_counters_alone_count_as_game_data()
    {
        using var factory = new SqliteTestDb();
        using var db = factory.CreateDbContext();
        db.PartyConfigs.Add(new PartyConfig { Counters = [Counter("Fear")] });
        db.SaveChanges();

        GameSystemUpgrade.Run(db, NullLogger.Instance);

        var config = db.PartyConfigs.Single();
        Assert.Equal("daggerheart", config.SystemId);
        Assert.Equal("fear", config.Counters.Single().Key);
    }

    [Theory]
    [InlineData(GameSystemRegistry.None)] // the GM's explicit "no system" is never second-guessed
    [InlineData("dnd5e")]                 // nor is any other made choice
    public void An_already_made_choice_is_never_overridden(string chosen)
    {
        using var factory = new SqliteTestDb();
        using var db = factory.CreateDbContext();
        db.PartyMembers.Add(Member(Counter("HP")));
        db.PartyConfigs.Add(new PartyConfig { SystemId = chosen });
        db.SaveChanges();

        GameSystemUpgrade.Run(db, NullLogger.Instance);

        Assert.Equal(chosen, db.PartyConfigs.Single().SystemId);
        Assert.Null(db.PartyMembers.Single().Counters.Single().Key); // and no backfill happens either
    }
}
