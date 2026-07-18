namespace RpgSceneMaker.Api.Services.Music;

/// <summary>
/// A pluggable music backend, mirroring how <c>ILightService</c> abstracts Tuya vs Hue. Spotify becomes one
/// implementation (<see cref="SpotifyMusicSource"/>, a thin adapter over the untouched <c>SpotifyClient</c>)
/// and local-file playback the other (<see cref="LocalMusicSource"/> over <c>LocalMusicPlayer</c>). The
/// <c>MusicRouter</c> picks a source per request and the transport/state endpoints, <c>SceneActivator</c> and
/// the AI façade all drive music through this interface — never a concrete provider.
/// </summary>
public interface IMusicSource
{
    /// <summary>Stable key identifying this source: "spotify" | "local". Used for routing, the scene
    /// <c>MusicSettings.Source</c> discriminator and the play-id source inference.</summary>
    string Key { get; }

    /// <summary>Whether this source can be used right now (Spotify: an account is connected; local: always,
    /// its device is created lazily on first play). Sources that aren't available are dropped from the
    /// <c>/music/state</c> "available" list and skipped by the fallback resolver.</summary>
    bool IsAvailable { get; }

    /// <summary>Start playback for a source-native id (a spotify: URI/link for Spotify, a
    /// <c>local:track:{id}</c> / <c>local:playlist:{id}</c> for local).</summary>
    Task PlayAsync(string id);

    /// <summary>Pause playback. <paramref name="throwOnNoDevice"/> distinguishes a deliberate pause button
    /// (true — surface a missing device) from a best-effort pause during scene stop / source switching
    /// (false — a missing device or nothing playing is a silent no-op).</summary>
    Task PauseAsync(bool throwOnNoDevice = true);

    Task ResumeAsync();
    Task NextAsync();
    Task PreviousAsync();

    /// <summary>Set the output volume, 0.0–1.0.</summary>
    Task SetVolumeAsync(double volume01);

    Task SetShuffleAsync(bool shuffle);

    /// <summary>Set the repeat mode: off | track | playlist.</summary>
    Task SetRepeatAsync(string mode);

    /// <summary>Current playback state, or null when this source has nothing active.</summary>
    Task<MusicState?> GetStateAsync();
}
