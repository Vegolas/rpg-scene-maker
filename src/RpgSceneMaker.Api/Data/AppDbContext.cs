using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<Sound> Sounds => Set<Sound>();
    public DbSet<GameEvent> Events => Set<GameEvent>();
    public DbSet<Screen> Screens => Set<Screen>();
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

        modelBuilder.Entity<GameEvent>(evt =>
        {
            evt.HasKey(e => e.Id);
            // Event ids appear in hand-typed /events/{id}/trigger URLs, so match them case-insensitively too.
            evt.Property(e => e.Id).UseCollation("NOCASE");
            evt.OwnsOne(e => e.Flash, b => b.ToJson());
            // The advanced timeline is a small object graph (sound + light clips) — one JSON column, like Flash.
            evt.OwnsOne(e => e.Timeline, timeline =>
            {
                timeline.ToJson();
                timeline.OwnsMany(t => t.Sounds);
                timeline.OwnsMany(t => t.Lights, lights => lights.OwnsOne(l => l.Effect));
            });
        });

        modelBuilder.Entity<Screen>(screen =>
        {
            screen.HasKey(s => s.Id);
            // Screen ids appear in /screens/{id} URLs (deep-linkable), so match them case-insensitively too.
            screen.Property(s => s.Id).UseCollation("NOCASE");
            // Tiles are a small ordered list of value objects — stored as one JSON column (like Scene.Lights).
            screen.OwnsMany(s => s.Tiles, b => b.ToJson());
        });

        modelBuilder.Entity<LightingConfig>(config =>
        {
            config.HasKey(c => c.Id);
            config.Property(c => c.Id).ValueGeneratedNever();
            config.OwnsOne(c => c.Hue, b => b.ToJson());
            config.OwnsOne(c => c.Tuya, b => b.ToJson());
            config.OwnsMany(c => c.Lights, b => b.ToJson());
            config.OwnsOne(c => c.DefaultLight, b => b.ToJson());
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
