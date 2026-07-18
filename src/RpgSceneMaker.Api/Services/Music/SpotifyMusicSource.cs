namespace RpgSceneMaker.Api.Services.Music;

/// <summary>The Spotify <see cref="IMusicSource"/>: a thin adapter over the existing <c>SpotifyClient</c>,
/// which is left entirely intact (setup/OAuth/devices/playlists/search stay Spotify-specific and are not part
/// of this seam). Registered scoped, matching <c>SpotifyClient</c>'s typed-HttpClient lifetime.</summary>
public sealed class SpotifyMusicSource(SpotifyClient spotify) : IMusicSource
{
    public string Key => "spotify";

    public bool IsAvailable => spotify.IsConnected;

    public Task PlayAsync(string id) => spotify.PlayAsync(id);
    public Task PauseAsync(bool throwOnNoDevice = true) => spotify.PauseAsync(throwOnNoDevice);
    public Task ResumeAsync() => spotify.ResumeAsync();
    public Task NextAsync() => spotify.NextAsync();
    public Task PreviousAsync() => spotify.PreviousAsync();
    public Task SetVolumeAsync(double volume01) => spotify.SetVolumeAsync(volume01);
    public Task SetShuffleAsync(bool shuffle) => spotify.SetShuffleAsync(shuffle);
    public Task SetRepeatAsync(string mode) => spotify.SetRepeatAsync(mode);

    public async Task<MusicState?> GetStateAsync()
    {
        var s = await spotify.GetStateAsync();
        // Spotify's context is only a URI (resolving a friendly name would cost another call and the panel
        // never showed it), so ContextName stays null; track/artist/device/progress carry through.
        return s is null
            ? null
            : new MusicState("spotify", s.IsPlaying, s.TrackName, s.ArtistName, ContextName: null,
                s.DeviceName, s.VolumePercent, s.ProgressSeconds, s.DurationSeconds, s.IsShuffling, s.Repeat);
    }
}
