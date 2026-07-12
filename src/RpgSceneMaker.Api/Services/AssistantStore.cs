using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// In-panel assistant settings persisted in SQLite. Mirrors <see cref="SpotifyStore"/>: the current value is
/// cached in memory and swapped atomically on save, so changes apply immediately and reads never race a
/// database reload. One active provider at a time (provider + key + model). The API key is write-only from
/// the panel's point of view — it is stored here and used to talk to the chosen AI backend, but never
/// returned by any endpoint.
/// </summary>
public class AssistantStore(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly Lock _lock = new();
    private AssistantConfig? _current;

    public AssistantConfig Current
    {
        get
        {
            lock (_lock) { return _current ??= Load(); }
        }
    }

    /// <summary>Save the provider, key and/or model. A null/empty value keeps the stored one (so the model or
    /// provider can change without re-pasting the key); non-empty values replace the stored ones after
    /// trimming.</summary>
    public void Save(string? provider, string? apiKey, string? model) =>
        Update(entity =>
        {
            if (!string.IsNullOrWhiteSpace(provider))
                entity.Provider = provider.Trim();
            if (!string.IsNullOrWhiteSpace(apiKey))
                entity.ApiKey = apiKey.Trim();
            if (!string.IsNullOrWhiteSpace(model))
                entity.Model = model.Trim();
        });

    /// <summary>Forget the API key (the assistant goes back to unconfigured). Provider + model are kept.</summary>
    public void Clear() =>
        Update(entity => entity.ApiKey = "");

    private void Update(Action<AssistantConfig> mutate)
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            var entity = db.AssistantConfigs.SingleOrDefault(c => c.Id == AssistantConfig.SingletonId);
            if (entity is null)
            {
                entity = new AssistantConfig();
                db.AssistantConfigs.Add(entity);
            }

            mutate(entity);
            db.SaveChanges();
            _current = entity;
        }
    }

    private AssistantConfig Load()
    {
        using var db = dbFactory.CreateDbContext();
        return db.AssistantConfigs.AsNoTracking().SingleOrDefault(c => c.Id == AssistantConfig.SingletonId)
               ?? new AssistantConfig();
    }
}
