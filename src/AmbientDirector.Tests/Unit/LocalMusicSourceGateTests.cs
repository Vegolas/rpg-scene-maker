using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Audio;
using AmbientDirector.Api.Services.Music;
using AmbientDirector.Tests.Store;
using NAudio.Wave;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>
/// The /music/state "available" gate: local is advertised only when the library has ≥1 track or a queue is
/// loaded — routing IsAvailable stays always-true so play on an empty library still errors precisely.
/// (The playing half of the gate needs a real audio device, so it is exercised by the manual verification,
/// not here; an idle player reports a null state, which is the branch these tests pin.)
/// </summary>
public class LocalMusicSourceGateTests
{
    private static LocalMusicSource Build(SqliteTestDb db, LocalMusicPlayer player) =>
        new(player,
            new MusicTrackStore(db),
            new MusicPlaylistStore(db),
            new MusicFileStorage(Path.Combine(Path.GetTempPath(), "rsm-tests", Path.GetRandomFileName())));

    [Fact]
    public async Task Not_advertised_with_an_empty_library_and_idle_player()
    {
        using var db = new SqliteTestDb();
        using var player = new LocalMusicPlayer(new NoopWavePlayerFactory()); // never played -> no device, null state
        var source = Build(db, player);

        Assert.True(source.IsAvailable);               // routing stays always-true
        Assert.False(await source.IsAdvertisedAsync()); // but the panel gate hides dead transport
    }

    [Fact]
    public async Task Advertised_once_the_library_has_a_track()
    {
        using var db = new SqliteTestDb();
        using var player = new LocalMusicPlayer(new NoopWavePlayerFactory());
        await new MusicTrackStore(db).UpsertAsync(new MusicTrack { Id = "tavern", Name = "Tavern" });

        Assert.True(await Build(db, player).IsAdvertisedAsync());
    }
}

/// <summary>A sink factory whose players do nothing — these tests build a <see cref="LocalMusicPlayer"/> but
/// never play, so the factory is never actually invoked; it just satisfies the constructor without opening any
/// real audio device.</summary>
file sealed class NoopWavePlayerFactory : IWavePlayerFactory
{
    public IWavePlayer Create(int desiredLatencyMs) => new NoopWavePlayer();
}

file sealed class NoopWavePlayer : IWavePlayer
{
    public PlaybackState PlaybackState => PlaybackState.Stopped;
    public WaveFormat OutputWaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    public float Volume { get; set; } = 1f;
#pragma warning disable CS0067 // required by the interface; never raised by this no-op
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
#pragma warning restore CS0067
    public void Init(IWaveProvider waveProvider) { }
    public void Play() { }
    public void Pause() { }
    public void Stop() { }
    public void Dispose() { }
}
