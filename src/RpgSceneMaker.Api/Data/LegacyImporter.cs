using System.Text.Json;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Data;

/// <summary>
/// One-time migration of the pre-SQLite persistence: lighting settings from
/// appsettings.json + settings.local.json, and scenes from scenes.json.
/// Runs at startup and only fills tables that are still empty, so the legacy
/// files can stay on disk as a backup without ever overwriting the database.
/// </summary>
public static class LegacyImporter
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static void Run(AppDbContext db, IConfiguration config, IHostEnvironment env, ILogger logger)
    {
        if (!db.LightingConfigs.Any())
        {
            // settings.local.json used to be a config overlay; layer it the same way for the import.
            var legacy = new ConfigurationBuilder()
                .AddConfiguration(config)
                .AddJsonFile(Path.Combine(env.ContentRootPath, "settings.local.json"), optional: true)
                .Build();

            var imported = new LightingConfig
            {
                Provider = legacy["Lighting:Provider"] ?? "tuya",
                Hue = legacy.GetSection("Hue").Get<HueConfig>() ?? new(),
                Tuya = legacy.GetSection("Tuya").Get<TuyaConfig>() ?? new(),
            };
            db.LightingConfigs.Add(imported);
            logger.LogInformation("Imported lighting settings into the database (provider: {Provider})", imported.Provider);
        }

        if (!db.Scenes.Any())
        {
            var scenesPath = Path.Combine(env.ContentRootPath, config["Scenes:FilePath"] ?? "scenes.json");
            if (File.Exists(scenesPath))
            {
                var scenes = JsonSerializer.Deserialize<List<Scene>>(File.ReadAllText(scenesPath), JsonOpts) ?? [];
                db.Scenes.AddRange(scenes);
                logger.LogInformation("Imported {Count} scenes from {Path} into the database", scenes.Count, scenesPath);
            }
        }

        db.SaveChanges();
    }
}
