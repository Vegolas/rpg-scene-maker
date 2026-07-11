using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>Events persisted in SQLite. Ids match case-insensitively (NOCASE collation).</summary>
public class EventStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<GameEvent>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Events.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
    }

    public async Task<GameEvent?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
    }

    public async Task UpsertAsync(GameEvent evt)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Events.SingleOrDefaultAsync(e => e.Id == evt.Id);
        if (existing is null)
        {
            db.Events.Add(evt);
        }
        else
        {
            existing.Name = evt.Name;
            existing.Flash = evt.Flash;
            existing.SoundEffects = evt.SoundEffects;
            existing.Image = evt.Image;
            existing.Timeline = evt.Timeline;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Events.Where(e => e.Id == id).ExecuteDeleteAsync() > 0;
    }
}
