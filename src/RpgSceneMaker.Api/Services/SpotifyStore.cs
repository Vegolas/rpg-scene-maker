using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Spotify connection state persisted in SQLite. Mirrors <see cref="SettingsStore"/>: the current
/// value is cached in memory and swapped atomically on save, so changes apply immediately and reads
/// never race a database reload.
/// </summary>
public class SpotifyStore(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly Lock _lock = new();
    private SpotifyConfig? _current;

    public SpotifyConfig Current
    {
        get
        {
            lock (_lock) { return _current ??= Load(); }
        }
    }

    /// <summary>Save the Client ID. If it changed, the refresh token and device are cleared
    /// (tokens minted for a different app are invalid).</summary>
    public void SaveConfig(string clientId)
    {
        Update(entity =>
        {
            if (!string.Equals(entity.ClientId, clientId, StringComparison.Ordinal))
            {
                entity.RefreshToken = "";
                entity.PreferredDeviceId = "";
                entity.PreferredDeviceName = "";
            }
            entity.ClientId = clientId;
        });
    }

    public void SaveTokens(string refreshToken) =>
        Update(entity => entity.RefreshToken = refreshToken);

    public void SaveDevice(string id, string name) =>
        Update(entity =>
        {
            entity.PreferredDeviceId = id;
            entity.PreferredDeviceName = name;
        });

    public void Disconnect() =>
        Update(entity =>
        {
            entity.RefreshToken = "";
            entity.PreferredDeviceId = "";
            entity.PreferredDeviceName = "";
        });

    private void Update(Action<SpotifyConfig> mutate)
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            var entity = db.SpotifyConfigs.SingleOrDefault(c => c.Id == SpotifyConfig.SingletonId);
            if (entity is null)
            {
                entity = new SpotifyConfig();
                db.SpotifyConfigs.Add(entity);
            }

            mutate(entity);
            db.SaveChanges();
            _current = entity;
        }
    }

    private SpotifyConfig Load()
    {
        using var db = dbFactory.CreateDbContext();
        return db.SpotifyConfigs.AsNoTracking().SingleOrDefault(c => c.Id == SpotifyConfig.SingletonId)
               ?? new SpotifyConfig();
    }
}
