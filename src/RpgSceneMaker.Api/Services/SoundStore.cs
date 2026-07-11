using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>Sound-effect metadata persisted in SQLite. Ids match case-insensitively (NOCASE collation).</summary>
public class SoundStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Sound>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Sounds.AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.Name).ToListAsync();
    }

    public async Task<Sound?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Sounds.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
    }

    public async Task UpsertAsync(Sound sound)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Sounds.SingleOrDefaultAsync(s => s.Id == sound.Id);
        if (existing is null)
        {
            db.Sounds.Add(sound);
        }
        else
        {
            existing.Name = sound.Name;
            existing.Category = sound.Category;
            existing.FileName = sound.FileName;
            existing.Volume = sound.Volume;
            existing.Loop = sound.Loop;
            existing.Image = sound.Image;
            existing.DurationMs = sound.DurationMs;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Sounds.Where(s => s.Id == id).ExecuteDeleteAsync() > 0;
    }
}
