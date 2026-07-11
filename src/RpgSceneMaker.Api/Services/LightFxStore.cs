using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>Reusable Light FX library persisted in SQLite. Ids match case-insensitively (NOCASE collation).</summary>
public class LightFxStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<LightFx>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.LightFxs.AsNoTracking().OrderBy(f => f.Name).ToListAsync();
    }

    public async Task<LightFx?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.LightFxs.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
    }

    public async Task UpsertAsync(LightFx fx)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.LightFxs.SingleOrDefaultAsync(f => f.Id == fx.Id);
        if (existing is null)
        {
            db.LightFxs.Add(fx);
        }
        else
        {
            existing.Name = fx.Name;
            existing.Keyframes = fx.Keyframes;
            existing.Loop = fx.Loop;
            existing.CycleMs = fx.CycleMs;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.LightFxs.Where(f => f.Id == id).ExecuteDeleteAsync() > 0;
    }
}
