using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<Sound> Sounds => Set<Sound>();
    public DbSet<LightingConfig> LightingConfigs => Set<LightingConfig>();
    public DbSet<SpotifyConfig> SpotifyConfigs => Set<SpotifyConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Scene>(scene =>
        {
            scene.HasKey(s => s.Id);
            // Scene ids were always matched case-insensitively (Stream Deck URLs are hand-typed).
            scene.Property(s => s.Id).UseCollation("NOCASE");
            scene.OwnsOne(s => s.Light, b => b.ToJson());
            scene.OwnsOne(s => s.Music, b => b.ToJson());
            scene.OwnsMany(s => s.Lights, lights =>
            {
                lights.ToJson();
                lights.OwnsOne(l => l.Effect);
            });
        });

        modelBuilder.Entity<Sound>(sound =>
        {
            sound.HasKey(s => s.Id);
            // Sound ids appear in hand-typed /sounds/{id}/play URLs, so match them case-insensitively too.
            sound.Property(s => s.Id).UseCollation("NOCASE");
        });

        modelBuilder.Entity<LightingConfig>(config =>
        {
            config.HasKey(c => c.Id);
            config.Property(c => c.Id).ValueGeneratedNever();
            config.OwnsOne(c => c.Hue, b => b.ToJson());
            config.OwnsOne(c => c.Tuya, b => b.ToJson());
            config.OwnsMany(c => c.Lights, b => b.ToJson());
            config.Navigation(c => c.Hue).IsRequired();
            config.Navigation(c => c.Tuya).IsRequired();
        });

        modelBuilder.Entity<SpotifyConfig>(config =>
        {
            config.HasKey(c => c.Id);
            config.Property(c => c.Id).ValueGeneratedNever();
            // Computed convenience flags — never stored.
            config.Ignore(c => c.IsConfigured);
            config.Ignore(c => c.IsConnected);
        });
    }
}
