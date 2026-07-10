using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Data;
using Xunit;

namespace RpgSceneMaker.Tests.Integration;

/// <summary>
/// Boots the real app (all middleware, real EF migrations) against a throwaway temp directory.
///
/// Overrides are pushed through environment variables because the app reads Database:Path,
/// Sounds:Path and Scenes:FilePath during startup — before WebApplicationFactory's
/// ConfigureAppConfiguration callbacks apply — and CreateBuilder reads env vars immediately.
/// The lighting config row is pre-seeded so LegacyImporter skips importing the repo's
/// settings.local.json (which would otherwise configure a real Hue bridge); Scenes:FilePath points
/// at a missing file so no starter scenes are seeded either. Integration tests share one xUnit
/// collection so these process-wide env vars are never set by two factories at once.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "rsm-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;

    /// <param name="apiKey">When set, the app requires this key on protected routes.</param>
    public ApiFactory(string? apiKey = null)
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");

        Environment.SetEnvironmentVariable("Database__Path", _dbPath);
        Environment.SetEnvironmentVariable("Sounds__Path", Path.Combine(_root, "sounds"));
        Environment.SetEnvironmentVariable("Scenes__FilePath", "does-not-exist.json");
        Environment.SetEnvironmentVariable("Security__ApiKey", apiKey ?? "");

        SeedDefaultLightingConfig();
    }

    // Pre-create the DB with a plain tuya (unconfigured) lighting config so LegacyImporter's
    // "if no config yet" branch never runs and never imports the repo's settings.local.json.
    private void SeedDefaultLightingConfig()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var db = new AppDbContext(options);
        db.Database.Migrate();
        if (!db.LightingConfigs.Any())
        {
            db.LightingConfigs.Add(new LightingConfig { Provider = "tuya" });
            db.SaveChanges();
        }
        SqliteConnection.ClearAllPools();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) { }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        Environment.SetEnvironmentVariable("Database__Path", null);
        Environment.SetEnvironmentVariable("Sounds__Path", null);
        Environment.SetEnvironmentVariable("Scenes__FilePath", null);
        Environment.SetEnvironmentVariable("Security__ApiKey", null);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>All integration tests share this collection so the process-wide env vars set by
/// <see cref="ApiFactory"/> are never written by two factories concurrently.</summary>
[CollectionDefinition("integration", DisableParallelization = true)]
public class IntegrationCollection;
