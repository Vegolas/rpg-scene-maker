using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>Local music-track metadata persisted in SQLite. Ids match case-insensitively (NOCASE
/// collation), like sounds. The music sibling of <see cref="SoundStore"/>.</summary>
public class MusicTrackStore(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<MusicTrack>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MusicTracks.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<MusicTrack?> GetAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MusicTracks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
    }

    public async Task UpsertAsync(MusicTrack track)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.MusicTracks.SingleOrDefaultAsync(t => t.Id == track.Id);
        if (existing is null)
        {
            db.MusicTracks.Add(track);
        }
        else
        {
            existing.Name = track.Name;
            existing.FileName = track.FileName;
            existing.DurationMs = track.DurationMs;
            existing.Artist = track.Artist;
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MusicTracks.Where(t => t.Id == id).ExecuteDeleteAsync() > 0;
    }
}
