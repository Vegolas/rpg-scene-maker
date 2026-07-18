using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

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
            new TuyaConfigDto(c.Tuya.Ip, c.Tuya.DeviceId, c.Tuya.LocalKey, c.Tuya.ProtocolVersion, c.Tuya.DpProfile),
            c.Lights.Select(l => new RegisteredLightDto(l.Key, l.Name, l.Provider, l.HueId)).ToList(),
            c.DefaultLight is { } d ? new DefaultLightDto(d.Power, d.Color, d.Brightness, d.Temperature) : null);
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
            entity.Lights = (dto.Lights ?? [])
                .Select(l => new RegisteredLight { Key = l.Key, Name = l.Name, Provider = l.Provider, HueId = l.HueId })
                .ToList();
            entity.DefaultLight = dto.DefaultLight is { } d
                ? new LightSettings { Power = d.Power, Color = d.Color, Brightness = d.Brightness, Temperature = d.Temperature }
                : null;

            db.SaveChanges();
            _current = entity;
        }
    }

    /// <summary>
    /// Stamp the first-run onboarding as done (now), persisted on the single-row config. Idempotent enough —
    /// re-stamping just moves the timestamp. Called when the wizard finishes and by the auto-complete path in
    /// GET /setup/onboarding for existing installs. Preserves the rest of the lighting config.
    /// </summary>
    public void MarkOnboardingDone()
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

            entity.OnboardingDoneUtc = DateTimeOffset.UtcNow;
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
