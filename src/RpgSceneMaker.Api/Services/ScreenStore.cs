using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>Screens (shortcut boards) persisted in SQLite. Ids match case-insensitively (NOCASE collation).</summary>
public class ScreenStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Screen>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Screens.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
    }

    public async Task<Screen?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Screens.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
    }

    public async Task UpsertAsync(Screen screen)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Screens.SingleOrDefaultAsync(s => s.Id == screen.Id);
        if (existing is null)
        {
            db.Screens.Add(screen);
        }
        else
        {
            existing.Name = screen.Name;
            existing.Tiles = screen.Tiles;
            existing.Image = screen.Image;
            existing.Compact = screen.Compact;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Screens.Where(s => s.Id == id).ExecuteDeleteAsync() > 0;
    }
}
