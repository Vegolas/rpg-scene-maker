using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services.Systems;

namespace AmbientDirector.Api.Data;

/// <summary>
/// Startup one-shot for the game-system setting (issue #127; the <see cref="LegacyImporter"/> idiom, run
/// right after it in Program.cs). Installs that used the party/bestiary/encounter layer before the setting
/// existed have <c>PartyConfig.SystemId == null</c> — which would hide the panel's Encounters tab on upgrade.
/// When any game data exists, stamp <c>"daggerheart"</c> (the only system whose presets the old UI offered)
/// and backfill <see cref="PartyCounter.Key"/> on every counter whose label matches a known preset label in
/// English or Polish (the two shipped locales while the presets were UI-hardcoded), so adjust-by-key and the
/// key-driven render pipeline (#128) work for pre-existing data.
/// </summary>
/// <remarks>
/// Idempotent by construction: it only ever acts when <c>SystemId</c> is null, and stamping makes it
/// non-null. An explicit "no system" is stored as the sentinel <see cref="GameSystemRegistry.None"/>, never
/// null — so a GM's deliberate choice is never overridden here. A fresh install (no game data) stays null
/// and simply starts gated until a system is picked in Settings.
/// </remarks>
public static class GameSystemUpgrade
{
    // Known preset labels → semantic key, English + Polish (matching BoardCanvas's old label matching plus
    // the pl.json preset strings). OrdinalIgnoreCase: labels were typed/localized text.
    private static readonly Dictionary<string, string> LabelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hp"] = "hp",
        ["health"] = "hp",
        ["stress"] = "stress",
        ["stres"] = "stress",
        ["armor"] = "armor",
        ["armour"] = "armor",
        ["pancerz"] = "armor",
        ["hope"] = "hope",
        ["nadzieja"] = "hope",
        ["fear"] = "fear",
        ["strach"] = "fear",
    };

    public static void Run(AppDbContext db, ILogger logger)
    {
        var config = db.PartyConfigs.SingleOrDefault(c => c.Id == PartyConfig.SingletonId);
        if (config?.SystemId is not null) return; // chosen (or "none") — never second-guess it

        var hasGameData = db.PartyMembers.Any() || db.Enemies.Any() || db.Encounters.Any() ||
                          config?.Counters is { Count: > 0 };
        if (!hasGameData) return; // fresh install — stays null until the GM picks a system in Settings

        if (config is null)
        {
            config = new PartyConfig();
            db.PartyConfigs.Add(config);
        }
        config.SystemId = "daggerheart";

        // Backfill keys everywhere a PartyCounter lives. The entities are tracked, so mutating the owned-JSON
        // counters in place rewrites the columns on save (the PartyStore.Adjust… pattern).
        var stamped = Backfill(config.Counters);
        foreach (var member in db.PartyMembers)
            stamped += Backfill(member.Counters);
        foreach (var enemy in db.Enemies)
            stamped += Backfill(enemy.Counters);
        foreach (var encounter in db.Encounters)
            foreach (var instance in encounter.Enemies ?? [])
                stamped += Backfill(instance.Counters);

        db.SaveChanges();
        logger.LogInformation(
            "Game-system upgrade: existing game data found — stamped 'daggerheart' and backfilled {Count} counter key(s).",
            stamped);
    }

    private static int Backfill(List<PartyCounter>? counters)
    {
        var stamped = 0;
        foreach (var counter in counters ?? [])
        {
            if (counter.Key is not null) continue;
            if (!LabelKeys.TryGetValue(counter.Label.Trim(), out var key)) continue;
            counter.Key = key;
            stamped++;
        }
        return stamped;
    }
}
