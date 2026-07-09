using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;

namespace RpgSceneMaker.Api.Services;

public record TuyaConfigDto(string Ip, string DeviceId, string LocalKey, string ProtocolVersion, string DpProfile);
public record HueConfigDto(string BridgeIp, string AppKey, List<string> LightIds);
public record LightingConfigDto(string Provider, HueConfigDto Hue, TuyaConfigDto Tuya);

/// <summary>
/// Lighting settings persisted in SQLite. The current value is cached in memory and
/// swapped atomically on save, so changes from the Settings page apply immediately
/// and reads never race a file reload.
/// </summary>
public class SettingsStore(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly Lock _lock = new();
    private LightingConfig? _current;

    public LightingConfig Current
    {
        get
        {
            lock (_lock) { return _current ??= Load(); }
        }
    }

    public LightingConfigDto GetDto()
    {
        var c = Current;
        return new LightingConfigDto(
            c.Provider,
            new HueConfigDto(c.Hue.BridgeIp, c.Hue.AppKey, c.Hue.LightIds),
            new TuyaConfigDto(c.Tuya.Ip, c.Tuya.DeviceId, c.Tuya.LocalKey, c.Tuya.ProtocolVersion, c.Tuya.DpProfile));
    }

    public void Save(LightingConfigDto dto)
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            var entity = db.LightingConfigs.SingleOrDefault(c => c.Id == LightingConfig.SingletonId);
            if (entity is null)
            {
                entity = new LightingConfig();
                db.LightingConfigs.Add(entity);
            }

            entity.Provider = dto.Provider;
            entity.Hue = new HueConfig
            {
                BridgeIp = dto.Hue.BridgeIp,
                AppKey = dto.Hue.AppKey,
                LightIds = dto.Hue.LightIds,
            };
            entity.Tuya = new TuyaConfig
            {
                Ip = dto.Tuya.Ip,
                DeviceId = dto.Tuya.DeviceId,
                LocalKey = dto.Tuya.LocalKey,
                ProtocolVersion = dto.Tuya.ProtocolVersion,
                DpProfile = dto.Tuya.DpProfile,
            };

            db.SaveChanges();
            _current = entity;
        }
    }

    private LightingConfig Load()
    {
        using var db = dbFactory.CreateDbContext();
        return db.LightingConfigs.AsNoTracking().SingleOrDefault(c => c.Id == LightingConfig.SingletonId)
               ?? new LightingConfig();
    }
}
