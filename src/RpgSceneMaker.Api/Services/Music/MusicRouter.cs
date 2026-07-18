using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Errors;

namespace RpgSceneMaker.Api.Services.Music;

/// <summary>
/// Picks the right <see cref="IMusicSource"/> per request and remembers the active one. The bare transport
/// endpoints, <c>SceneActivator</c> and the AI façade all go through here rather than a concrete provider:
/// <see cref="PlayAsync"/> infers the source from the id shape (or an explicit key), records it as active and
/// pauses the other source best-effort (only one thing plays at a time); the rest resolve a source (explicit
/// <c>?source=</c> → active → fallback of Spotify-if-connected-else-local). Scoped, like <c>ILightService</c>,
/// so it composes both scoped sources within the request scope.
/// </summary>
public sealed class MusicRouter(IEnumerable<IMusicSource> sources, MusicSourceState active)
{
    /// <summary>The keys of the sources usable right now (Spotify only when connected; local always).</summary>
    public IReadOnlyList<string> AvailableKeys() =>
        sources.Where(s => s.IsAvailable).Select(s => s.Key).ToList();

    /// <summary>Resolve a source for a transport/state op: an explicit key wins (even if unavailable, so the
    /// caller gets that source's real error); otherwise the active source if still available; otherwise the
    /// fallback — Spotify when connected, else local.</summary>
    public IMusicSource Resolve(string? key = null)
    {
        if (!string.IsNullOrWhiteSpace(key))
            return Find(key) ?? throw new ValidationException("error.music.unknownSource", key);
        if (active.ActiveSource is { } a && Find(a) is { IsAvailable: true } activeSource)
            return activeSource;
        return Find("spotify") is { IsAvailable: true } spotify ? spotify : Find("local")!;
    }

    /// <summary>The source to pause when a scene stops: the active source if still available, else Spotify
    /// when connected (matching the old best-effort pause), else null — nothing is playing to pause.</summary>
    public IMusicSource? ResolveActiveOrNull()
    {
        if (active.ActiveSource is { } a && Find(a) is { IsAvailable: true } activeSource)
            return activeSource;
        return Find("spotify") is { IsAvailable: true } spotify ? spotify : null;
    }

    /// <summary>Play an id, inferring the source from its shape unless <paramref name="sourceKey"/> forces one.
    /// On success remembers the active source and pauses the others (best-effort). Returns the source key used.</summary>
    public async Task<string> PlayAsync(string id, string? sourceKey = null)
    {
        var key = string.IsNullOrWhiteSpace(sourceKey) ? Infer(id) : sourceKey!;
        var source = Find(key) ?? throw new ValidationException("error.music.unknownSource", key);
        await source.PlayAsync(id);
        active.ActiveSource = source.Key;
        await PauseOthersAsync(source.Key);
        return source.Key;
    }

    /// <summary>Build the <c>/music/state</c> payload: the resolved source's state (or a "nothing playing"
    /// default) plus the source key and the available-sources list.</summary>
    public async Task<MusicStateDto> GetStateAsync(string? sourceKey = null)
    {
        var source = Resolve(sourceKey);
        var state = await source.GetStateAsync();
        var available = AvailableKeys();
        return state is null
            ? new MusicStateDto(source.Key, available, false, null, null, null, null, null, null, null, false, "off")
            : new MusicStateDto(state.Source, available, state.IsPlaying, state.TrackName, state.ArtistName,
                state.ContextName, state.DeviceName, state.VolumePercent, state.ProgressSeconds,
                state.DurationSeconds, state.IsShuffling, state.Repeat);
    }

    // Pause every other available source so switching sources leaves only the new one playing. Best-effort:
    // a source that can't be paused (no device, nothing playing) is ignored, never failing the play.
    private async Task PauseOthersAsync(string keepKey)
    {
        foreach (var s in sources.Where(s => !s.Key.Equals(keepKey, StringComparison.OrdinalIgnoreCase) && s.IsAvailable))
        {
            try { await s.PauseAsync(throwOnNoDevice: false); }
            catch { /* best-effort */ }
        }
    }

    private IMusicSource? Find(string key) =>
        sources.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    // Infer the source from a play id: a spotify: URI/link → Spotify, a local: id → local, else a bad request.
    private static string Infer(string id) =>
        SpotifyClient.IsSpotifyUri(id) ? "spotify"
        : LocalMusicId.IsLocal(id) ? "local"
        : throw new ValidationException("error.music.unknownPlayId", id);
}
