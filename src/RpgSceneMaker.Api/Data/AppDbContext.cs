using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<LightingConfig> LightingConfigs => Set<LightingConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Scene>(scene =>
        {
            scene.HasKey(s => s.Id);
            // Scene ids were always matched case-insensitively (Stream Deck URLs are hand-typed).
            scene.Property(s => s.Id).UseCollation("NOCASE");
            scene.OwnsOne(s => s.Light, b => b.ToJson());
            scene.OwnsOne(s => s.Music, b => b.ToJson());
        });

        modelBuilder.Entity<LightingConfig>(config =>
        {
            config.HasKey(c => c.Id);
            config.Property(c => c.Id).ValueGeneratedNever();
            config.OwnsOne(c => c.Hue, b => b.ToJson());
            config.OwnsOne(c => c.Tuya, b => b.ToJson());
            config.Navigation(c => c.Hue).IsRequired();
            config.Navigation(c => c.Tuya).IsRequired();
        });
    }
}
