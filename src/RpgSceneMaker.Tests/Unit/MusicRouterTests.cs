using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Services.Music;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class MusicRouterTests
{
    // A recording IMusicSource so the router's decisions (which source, active/pause) can be asserted.
    private sealed class FakeSource(string key, bool available = true, MusicState? state = null) : IMusicSource
    {
        public string Key => key;
        public bool IsAvailable { get; set; } = available;
        public string? PlayedId { get; private set; }
        public bool Paused { get; private set; }

        public Task PlayAsync(string id) { PlayedId = id; return Task.CompletedTask; }
        public Task PauseAsync(bool throwOnNoDevice = true) { Paused = true; return Task.CompletedTask; }
        public Task ResumeAsync() => Task.CompletedTask;
        public Task NextAsync() => Task.CompletedTask;
        public Task PreviousAsync() => Task.CompletedTask;
        public Task SetVolumeAsync(double volume01) => Task.CompletedTask;
        public Task SetShuffleAsync(bool shuffle) => Task.CompletedTask;
        public Task SetRepeatAsync(string mode) => Task.CompletedTask;
        public Task<MusicState?> GetStateAsync() => Task.FromResult(state);
    }

    private static (MusicRouter Router, FakeSource Spotify, FakeSource Local, MusicSourceState State) Build(
        bool spotifyAvailable = true, MusicState? spotifyState = null, MusicState? localState = null)
    {
        var spotify = new FakeSource("spotify", spotifyAvailable, spotifyState);
        var local = new FakeSource("local", true, localState);
        var state = new MusicSourceState();
        return (new MusicRouter([spotify, local], state), spotify, local, state);
    }

    [Fact]
    public void AvailableKeys_reflects_source_availability()
    {
        var (router, _, _, _) = Build(spotifyAvailable: false);
        Assert.Equal(["local"], router.AvailableKeys());
    }

    [Fact]
    public async Task Play_infers_spotify_from_a_spotify_uri_and_records_active()
    {
        var (router, spotify, local, state) = Build();

        var used = await router.PlayAsync("spotify:track:abc");

        Assert.Equal("spotify", used);
        Assert.Equal("spotify:track:abc", spotify.PlayedId);
        Assert.Equal("spotify", state.ActiveSource);
        Assert.True(local.Paused);   // switching sources pauses the other one
        Assert.Null(local.PlayedId);
    }

    [Fact]
    public async Task Play_infers_local_from_a_local_id_and_pauses_spotify()
    {
        var (router, spotify, local, state) = Build();

        var used = await router.PlayAsync("local:playlist:combat");

        Assert.Equal("local", used);
        Assert.Equal("local:playlist:combat", local.PlayedId);
        Assert.Equal("local", state.ActiveSource);
        Assert.True(spotify.Paused);
    }

    [Fact]
    public async Task Play_rejects_an_unrecognized_id()
    {
        var (router, _, _, _) = Build();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => router.PlayAsync("http://kenku/soundboard"));
        Assert.Equal("error.music.unknownPlayId", ex.Code);
    }

    [Fact]
    public void Resolve_falls_back_to_spotify_when_connected_else_local()
    {
        var (connected, _, _, _) = Build(spotifyAvailable: true);
        Assert.Equal("spotify", connected.Resolve().Key);

        var (offline, _, _, _) = Build(spotifyAvailable: false);
        Assert.Equal("local", offline.Resolve().Key);
    }

    [Fact]
    public void Resolve_prefers_the_active_source_when_still_available()
    {
        var (router, _, _, state) = Build();
        state.ActiveSource = "local";
        Assert.Equal("local", router.Resolve().Key);
    }

    [Fact]
    public void Resolve_honors_an_explicit_key_even_if_unavailable()
    {
        var (router, _, _, _) = Build(spotifyAvailable: false);
        Assert.Equal("spotify", router.Resolve("spotify").Key);   // explicit wins so the caller gets its real error
        Assert.Throws<ValidationException>(() => router.Resolve("bogus"));
    }

    [Fact]
    public async Task GetState_reports_source_and_available_when_nothing_is_playing()
    {
        var (router, _, _, _) = Build(spotifyAvailable: true);
        var dto = await router.GetStateAsync();

        Assert.False(dto.IsPlaying);
        Assert.Equal("spotify", dto.Source);
        Assert.Equal(["spotify", "local"], dto.Available);
        Assert.Equal("off", dto.Repeat);
    }

    [Fact]
    public async Task GetState_flattens_a_playing_sources_state()
    {
        var localState = new MusicState("local", IsPlaying: true, TrackName: "Tavern", ArtistName: "Bard",
            ContextName: "Combat", DeviceName: null, VolumePercent: 42, ProgressSeconds: 3, DurationSeconds: 60,
            IsShuffling: true, Repeat: "playlist");
        var (router, _, _, state) = Build(spotifyAvailable: false, localState: localState);
        state.ActiveSource = "local";

        var dto = await router.GetStateAsync();

        Assert.True(dto.IsPlaying);
        Assert.Equal("local", dto.Source);
        Assert.Equal("Tavern", dto.TrackName);
        Assert.Equal("Combat", dto.ContextName);
        Assert.Equal(42, dto.VolumePercent);
        Assert.Equal("playlist", dto.Repeat);
        Assert.Equal(["local"], dto.Available);
    }
}
