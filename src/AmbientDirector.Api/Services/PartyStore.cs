using Microsoft.EntityFrameworkCore;
using AmbientDirector.Api.Data;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

/// <summary>The party tracker's persistence (issue #88, Phase 3): per-person <see cref="PartyMember"/> rows
/// plus the single-row <see cref="PartyConfig"/> of table-level counters. Member ids match case-insensitively
/// (NOCASE collation). Mirrors <see cref="BoardStore"/>. The <c>Adjust…</c> methods load <b>tracked</b> (not
/// AsNoTracking) so mutating a counter's value persists the owning JSON column on save.</summary>
public class PartyStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<PartyMember>> GetMembersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        // Roster order: SortOrder ascending, ties broken by id (a stable, deterministic tiebreak).
        return await db.PartyMembers.AsNoTracking()
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();
    }

    public async Task<PartyMember?> GetMemberAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.PartyMembers.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id);
    }

    public async Task UpsertMemberAsync(PartyMember member)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.PartyMembers.SingleOrDefaultAsync(m => m.Id == member.Id);
        if (existing is null)
        {
            db.PartyMembers.Add(member);
        }
        else
        {
            existing.Name = member.Name;
            existing.Portrait = member.Portrait;
            existing.SortOrder = member.SortOrder;
            existing.Counters = member.Counters;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteMemberAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.PartyMembers.Where(m => m.Id == id).ExecuteDeleteAsync() > 0;
    }

    // ---- enemies: the encounter's opposing roster (issue #120), the twin of the member methods above ----

    public async Task<List<Enemy>> GetEnemiesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        // Roster order: SortOrder ascending, ties broken by id (a stable, deterministic tiebreak).
        return await db.Enemies.AsNoTracking()
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync();
    }

    public async Task<Enemy?> GetEnemyAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Enemies.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
    }

    public async Task UpsertEnemyAsync(Enemy enemy)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Enemies.SingleOrDefaultAsync(e => e.Id == enemy.Id);
        if (existing is null)
        {
            db.Enemies.Add(enemy);
        }
        else
        {
            existing.Name = enemy.Name;
            existing.Portrait = enemy.Portrait;
            existing.SortOrder = enemy.SortOrder;
            existing.Counters = enemy.Counters;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteEnemyAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Enemies.Where(e => e.Id == id).ExecuteDeleteAsync() > 0;
    }

    /// <summary>Adjust one of an enemy's counters (by semantic key or label) by a delta or to an absolute
    /// value, clamped into range, and return the updated enemy. Throws <see cref="NotFoundException"/> for an
    /// unknown enemy or counter.</summary>
    public async Task<Enemy> AdjustEnemyCounterAsync(string id, string token, int? delta, int? value)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var enemy = await db.Enemies.SingleOrDefaultAsync(e => e.Id == id)
            ?? throw new NotFoundException("error.party.enemyNotFound", id);
        // In-place mutation of a tracked owned-JSON entity is detected by change tracking and rewrites the column.
        AdjustInList(enemy.Counters ??= [], token, delta, value);
        await db.SaveChangesAsync();
        return enemy;
    }

    /// <summary>The stored game-system id, RAW (issue #127): null = never chosen, "none" = explicitly no
    /// system, else a system id (possibly no longer registered). Callers normalize through
    /// <see cref="Systems.GameSystemRegistry.Find"/> for the wire.</summary>
    public async Task<string?> GetSystemIdAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var config = await db.PartyConfigs.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == PartyConfig.SingletonId);
        return config?.SystemId;
    }

    /// <summary>Persist the game-system choice ("none" for an explicit no-system — never write null back,
    /// or the startup upgrade would re-stamp daggerheart). Creates the singleton row when missing.</summary>
    public async Task SaveSystemIdAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var config = await db.PartyConfigs.SingleOrDefaultAsync(c => c.Id == PartyConfig.SingletonId);
        if (config is null)
        {
            db.PartyConfigs.Add(new PartyConfig { SystemId = id });
        }
        else
        {
            config.SystemId = id;
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<PartyCounter>> GetTableCountersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var config = await db.PartyConfigs.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == PartyConfig.SingletonId);
        return config?.Counters ?? [];
    }

    public async Task SaveTableCountersAsync(List<PartyCounter> counters)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var config = await db.PartyConfigs.SingleOrDefaultAsync(c => c.Id == PartyConfig.SingletonId);
        if (config is null)
        {
            db.PartyConfigs.Add(new PartyConfig { Counters = counters });
        }
        else
        {
            config.Counters = counters;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Adjust one of a member's counters (by semantic key or label) by a delta or to an absolute
    /// value, clamped into range, and return the updated member. Throws <see cref="NotFoundException"/> for an
    /// unknown member or counter.</summary>
    public async Task<PartyMember> AdjustMemberCounterAsync(string id, string token, int? delta, int? value)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var member = await db.PartyMembers.SingleOrDefaultAsync(m => m.Id == id)
            ?? throw new NotFoundException("error.party.playerNotFound", id);
        // In-place mutation of a tracked owned-JSON entity is detected by change tracking and rewrites the column.
        AdjustInList(member.Counters ??= [], token, delta, value);
        await db.SaveChangesAsync();
        return member;
    }

    /// <summary>Adjust one of the table-level counters (by semantic key or label), clamped into range, and
    /// return the updated list. Throws <see cref="NotFoundException"/> if no counter matches.</summary>
    public async Task<List<PartyCounter>> AdjustTableCounterAsync(string token, int? delta, int? value)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var config = await db.PartyConfigs.SingleOrDefaultAsync(c => c.Id == PartyConfig.SingletonId);
        // When there is no row yet there are no counters, so the find below throws counterNotFound before we
        // ever reach SaveChanges — nothing to persist.
        var counters = config?.Counters ?? [];
        AdjustInList(counters, token, delta, value);
        await db.SaveChangesAsync();
        return counters;
    }

    // Find the counter — semantic Key first, then Label, both case-insensitive (issue #127: labels are
    // localized, so ?counter=fear must hit "Strach" on a Polish table) — apply the delta or the absolute
    // value, and clamp into [0, Max ?? 999]. Shared by the member/enemy/table adjust paths. The caller has
    // already ensured exactly one of delta/value is set (the endpoint's XOR guard). Throws on a miss.
    internal static void AdjustInList(List<PartyCounter> counters, string token, int? delta, int? value)
    {
        var counter = FindCounter(counters, token)
            ?? throw new NotFoundException("error.party.counterNotFound", token);
        var target = value ?? counter.Value + (delta ?? 0);
        counter.Value = Math.Clamp(target, 0, counter.Max ?? 999);
    }

    /// <summary>The shared adjust-token resolution: key match wins over label match (a counter's key is
    /// unique, so a token that is some counter's key never falls through to another's label).</summary>
    internal static PartyCounter? FindCounter(List<PartyCounter> counters, string token) =>
        counters.FirstOrDefault(c => c.Key is not null && string.Equals(c.Key, token, StringComparison.OrdinalIgnoreCase))
        ?? counters.FirstOrDefault(c => string.Equals(c.Label, token, StringComparison.OrdinalIgnoreCase));
}
