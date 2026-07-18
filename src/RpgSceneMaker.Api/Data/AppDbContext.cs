using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<Sound> Sounds => Set<Sound>();
    public DbSet<MusicTrack> MusicTracks => Set<MusicTrack>();
    public DbSet<MusicPlaylist> MusicPlaylists => Set<MusicPlaylist>();
    public DbSet<GameEvent> Events => Set<GameEvent>();
    public DbSet<Screen> Screens => Set<Screen>();
    public DbSet<LightFx> LightFxs => Set<LightFx>();
    public DbSet<LightingConfig> LightingConfigs => Set<LightingConfig>();
    public DbSet<SpotifyConfig> SpotifyConfigs => Set<SpotifyConfig>();
    public DbSet<AssistantConfig> AssistantConfigs => Set<AssistantConfig>();
    public DbSet<AssistantConversation> AssistantConversations => Set<AssistantConversation>();
    public DbSet<FreesoundConfig> FreesoundConfigs => Set<FreesoundConfig>();

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
                // Effect (and its "custom" keyframe list) live inside the same JSON document.
                lights.OwnsOne(l => l.Effect, fx => fx.OwnsMany(e => e.Keyframes));
            });
        });

        modelBuilder.Entity<Sound>(sound =>
        {
            sound.HasKey(s => s.Id);
            // Sound ids appear in hand-typed /sounds/{id}/play URLs, so match them case-insensitively too.
            sound.Property(s => s.Id).UseCollation("NOCASE");
        });

        modelBuilder.Entity<MusicTrack>(track =>
        {
            track.HasKey(t => t.Id);
            // Track ids appear in /music/library/tracks/{id} URLs and local:track:{id} play ids — case-insensitive.
            track.Property(t => t.Id).UseCollation("NOCASE");
        });

        modelBuilder.Entity<MusicPlaylist>(playlist =>
        {
            playlist.HasKey(p => p.Id);
            playlist.Property(p => p.Id).UseCollation("NOCASE");
            // TrackIds (a List<string>) maps to a JSON column by convention, like Scene.SoundEffects.
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
                timeline.OwnsMany(t => t.Lights, lights =>
                    lights.OwnsOne(l => l.Effect, fx => fx.OwnsMany(e => e.Keyframes)));
            });
            // What the lights do when the event finishes — one small JSON column, like Flash.
            evt.OwnsOne(e => e.After, b => b.ToJson());
        });

        modelBuilder.Entity<Screen>(screen =>
        {
            screen.HasKey(s => s.Id);
            // Screen ids appear in /screens/{id} URLs (deep-linkable), so match them case-insensitively too.
            screen.Property(s => s.Id).UseCollation("NOCASE");
            // Tiles are a small ordered list of value objects — stored as one JSON column (like Scene.Lights).
            screen.OwnsMany(s => s.Tiles, b => b.ToJson());
        });

        modelBuilder.Entity<LightFx>(fx =>
        {
            fx.HasKey(f => f.Id);
            // FX ids appear in hand-typed /lightfx/{id}/… URLs, so match them case-insensitively too.
            fx.Property(f => f.Id).UseCollation("NOCASE");
            // The keyframe sequence is a small ordered list of value objects — one JSON column (like Scene.Lights).
            fx.OwnsMany(f => f.Keyframes, b => b.ToJson());
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

        modelBuilder.Entity<AssistantConfig>(config =>
        {
            config.HasKey(c => c.Id);
            config.Property(c => c.Id).ValueGeneratedNever();
            // Computed convenience flag — never stored.
            config.Ignore(c => c.IsConfigured);
        });

        modelBuilder.Entity<AssistantConversation>(convo =>
        {
            convo.HasKey(c => c.Id);
            convo.Property(c => c.Id).ValueGeneratedNever();
            // TranscriptJson / HistoryJson are plain string columns — the assistant service serializes the
            // (polymorphic) transcript + history itself, so they are deliberately NOT EF owned-JSON mapped.
        });

        modelBuilder.Entity<FreesoundConfig>(config =>
        {
            config.HasKey(c => c.Id);
            config.Property(c => c.Id).ValueGeneratedNever();
            // Computed convenience flag — never stored.
            config.Ignore(c => c.IsConfigured);
        });
    }
}
