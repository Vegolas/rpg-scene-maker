using Microsoft.EntityFrameworkCore;
using AmbientDirector.Api.Data;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

/// <summary>Scenes persisted in SQLite. Ids match case-insensitively (NOCASE collation).</summary>
public class SceneStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Scene>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Scenes.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
    }

    public async Task<Scene?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Scenes.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
    }

    public async Task UpsertAsync(Scene scene)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Scenes.SingleOrDefaultAsync(s => s.Id == scene.Id);
        if (existing is null)
        {
            db.Scenes.Add(scene);
        }
        else
        {
            existing.Name = scene.Name;
            existing.Light = scene.Light;
            existing.Lights = scene.Lights;
            existing.Music = scene.Music;
            existing.SoundEffects = scene.SoundEffects;
            existing.Image = scene.Image;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Scenes.Where(s => s.Id == id).ExecuteDeleteAsync() > 0;
    }
}
