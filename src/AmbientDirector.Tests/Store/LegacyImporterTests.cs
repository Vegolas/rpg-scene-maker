using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using AmbientDirector.Api.Data;
using Xunit;

namespace AmbientDirector.Tests.Store;

public class LegacyImporterTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "rsm-legacy", Guid.NewGuid().ToString("N"));

    public LegacyImporterTests() => Directory.CreateDirectory(_contentRoot);

    public void Dispose()
    {
        try { Directory.Delete(_contentRoot, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeEnv(string contentRoot) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
    }

    private IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private void WriteScenes(string json) =>
        File.WriteAllText(Path.Combine(_contentRoot, "scenes.json"), json);

    [Fact]
    public void Imports_scenes_and_lighting_config_on_empty_db()
    {
        using var factory = new SqliteTestDb();
        WriteScenes("""[{"id":"tavern","name":"Tavern"},{"id":"forest","name":"Forest"}]""");

        using (var db = factory.CreateDbContext())
            LegacyImporter.Run(db, Config(("Lighting:Provider", "hue")), new FakeEnv(_contentRoot), NullLogger.Instance);

        using (var db = factory.CreateDbContext())
        {
            Assert.Equal(2, db.Scenes.Count());
            Assert.Equal("hue", db.LightingConfigs.Single().Provider);
        }
    }

    [Fact]
    public void Second_run_does_not_duplicate()
    {
        using var factory = new SqliteTestDb();
        WriteScenes("""[{"id":"tavern","name":"Tavern"}]""");

        using (var db = factory.CreateDbContext())
            LegacyImporter.Run(db, Config(), new FakeEnv(_contentRoot), NullLogger.Instance);
        using (var db = factory.CreateDbContext())
            LegacyImporter.Run(db, Config(), new FakeEnv(_contentRoot), NullLogger.Instance);

        using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1, db.Scenes.Count());
            Assert.Single(db.LightingConfigs);
        }
    }

    [Fact]
    public void Missing_scenes_file_still_imports_lighting_config()
    {
        using var factory = new SqliteTestDb();
        // No scenes.json written to the content root.

        using (var db = factory.CreateDbContext())
            LegacyImporter.Run(db, Config(("Lighting:Provider", "tuya")), new FakeEnv(_contentRoot), NullLogger.Instance);

        using (var db = factory.CreateDbContext())
        {
            Assert.Empty(db.Scenes);
            Assert.Equal("tuya", db.LightingConfigs.Single().Provider);
        }
    }
}
