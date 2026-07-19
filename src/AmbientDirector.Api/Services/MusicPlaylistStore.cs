using Microsoft.EntityFrameworkCore;
using AmbientDirector.Api.Data;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

/// <summary>Local music-playlist metadata persisted in SQLite (a name + an ordered list of track ids). Ids
/// match case-insensitively (NOCASE collation), like tracks and sounds.</summary>
public class MusicPlaylistStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<MusicPlaylist>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MusicPlaylists.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<MusicPlaylist?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MusicPlaylists.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
    }

    public async Task UpsertAsync(MusicPlaylist playlist)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.MusicPlaylists.SingleOrDefaultAsync(p => p.Id == playlist.Id);
        if (existing is null)
        {
            db.MusicPlaylists.Add(playlist);
        }
        else
        {
            existing.Name = playlist.Name;
            existing.TrackIds = playlist.TrackIds;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MusicPlaylists.Where(p => p.Id == id).ExecuteDeleteAsync() > 0;
    }
}
