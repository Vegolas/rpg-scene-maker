using Microsoft.EntityFrameworkCore;
using AmbientDirector.Api.Data;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

/// <summary>Encounters (prepped fights: heroes + enemy instances + background + optional scene/event) persisted
/// in SQLite. Ids match case-insensitively (NOCASE collation). Mirrors <see cref="BoardStore"/>; the
/// <c>Adjust…</c>/<c>Reset…</c> methods load <b>tracked</b> (not AsNoTracking) so mutating an instance's counter
/// value persists the owning JSON column on save (the <see cref="PartyStore"/> idiom).</summary>
public class EncounterStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Encounter>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        // List order: SortOrder ascending, ties broken by id (a stable, deterministic tiebreak).
        return await db.Encounters.AsNoTracking()
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync();
    }

    public async Task<Encounter?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
    }

    public async Task UpsertAsync(Encounter encounter)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Encounters.SingleOrDefaultAsync(e => e.Id == encounter.Id);
        if (existing is null)
        {
            db.Encounters.Add(encounter);
        }
        else
        {
            existing.Name = encounter.Name;
            existing.SortOrder = encounter.SortOrder;
            existing.HeroIds = encounter.HeroIds;
            existing.Enemies = encounter.Enemies;
            existing.BackgroundImage = encounter.BackgroundImage;
            existing.ActivateSceneId = encounter.ActivateSceneId;
            existing.ActivateEventId = encounter.ActivateEventId;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Encounters.Where(e => e.Id == id).ExecuteDeleteAsync() > 0;
    }

    /// <summary>Adjust one counter of one enemy instance by a delta or to an absolute value, clamped into
    /// <c>[0, Max ?? 999]</c>, and return the updated encounter. Throws <see cref="NotFoundException"/> for an
    /// unknown encounter, instance or counter.</summary>
    public async Task<Encounter> AdjustEnemyInstanceAsync(string encounterId, string instanceId, string label, int? delta, int? value)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var encounter = await db.Encounters.SingleOrDefaultAsync(e => e.Id == encounterId)
            ?? throw new NotFoundException("error.encounter.notFound", encounterId);
        var instance = (encounter.Enemies ??= []).FirstOrDefault(i =>
            string.Equals(i.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException("error.encounter.enemyInstanceNotFound", instanceId);

        // In-place mutation of a tracked owned-JSON entity is detected by change tracking and rewrites the column.
        var counter = (instance.Counters ??= []).FirstOrDefault(c =>
            string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotFoundException("error.party.counterNotFound", label);
        var target = value ?? counter.Value + (delta ?? 0);
        counter.Value = Math.Clamp(target, 0, counter.Max ?? 999);

        await db.SaveChangesAsync();
        return encounter;
    }

    /// <summary>Reset every enemy instance in the encounter to its <b>statblock's starting values</b> before
    /// re-running the fight. Counters are tracked <em>upward</em> (HP = damage marked, Stress marked), so
    /// "fresh" is each counter's starting Value — <em>not</em> its Max. Re-seed by label from the bestiary
    /// template (matched by <see cref="EncounterEnemy.EnemyId"/>); a counter with no template match — or an
    /// instance whose template was since deleted — falls back to 0, the undamaged value in that count-up model.
    /// Returns the updated encounter. Throws <see cref="NotFoundException"/> for an unknown encounter.</summary>
    public async Task<Encounter> ResetEnemiesAsync(string encounterId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var encounter = await db.Encounters.SingleOrDefaultAsync(e => e.Id == encounterId)
            ?? throw new NotFoundException("error.encounter.notFound", encounterId);

        // Load the bestiary templates behind the instances (NOCASE id column → case-insensitive IN match).
        var enemyIds = (encounter.Enemies ?? []).Select(i => i.EnemyId).Distinct().ToList();
        var templates = await db.Enemies.AsNoTracking()
            .Where(e => enemyIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var instance in encounter.Enemies ?? [])
        {
            templates.TryGetValue(instance.EnemyId, out var template);
            foreach (var counter in instance.Counters ?? [])
            {
                var start = template?.Counters?.FirstOrDefault(c =>
                    string.Equals(c.Label, counter.Label, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;
                counter.Value = Math.Clamp(start, 0, counter.Max ?? 999);
            }
        }
        await db.SaveChangesAsync();
        return encounter;
    }
}
