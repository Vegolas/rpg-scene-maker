using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Freesound connection state persisted in SQLite. Modeled line-for-line on <see cref="AssistantStore"/>: the
/// current value is cached in memory and swapped atomically on save, so changes apply immediately and reads
/// never race a database reload. The API token is write-only from the panel's point of view — it is stored
/// here and used to talk to Freesound, but never returned by any endpoint.
/// </summary>
public class FreesoundStore(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly Lock _lock = new();
    private FreesoundConfig? _current;

    public FreesoundConfig Current
    {
        get
        {
            lock (_lock) { return _current ??= Load(); }
        }
    }

    /// <summary>Save the API token. A null/empty value is ignored (keeps the stored token); a non-empty value
    /// replaces the stored one after trimming.</summary>
    public void Save(string? apiKey) =>
        Update(entity =>
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                entity.ApiKey = apiKey.Trim();
        });

    /// <summary>Forget the API token (Freesound goes back to unconfigured).</summary>
    public void Clear() =>
        Update(entity => entity.ApiKey = "");

    private void Update(Action<FreesoundConfig> mutate)
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            var entity = db.FreesoundConfigs.SingleOrDefault(c => c.Id == FreesoundConfig.SingletonId);
            if (entity is null)
            {
                entity = new FreesoundConfig();
                db.FreesoundConfigs.Add(entity);
            }

            mutate(entity);
            db.SaveChanges();
            _current = entity;
        }
    }

    private FreesoundConfig Load()
    {
        using var db = dbFactory.CreateDbContext();
        return db.FreesoundConfigs.AsNoTracking().SingleOrDefault(c => c.Id == FreesoundConfig.SingletonId)
               ?? new FreesoundConfig();
    }
}
