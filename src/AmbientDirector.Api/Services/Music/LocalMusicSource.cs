using AmbientDirector.Api.Errors;

namespace AmbientDirector.Api.Services.Music;

/// <summary>The local-file <see cref="IMusicSource"/>: resolves a <c>local:track:{id}</c> / <c>local:playlist:{id}</c>
/// id against the library stores and drives the singleton <see cref="LocalMusicPlayer"/> (the server's own
/// audio device). Always "available" — its output device is created lazily on first play, so a host with no
/// audio device fails only at play time (a clean 503), never at construction. Registered scoped.</summary>
public sealed class LocalMusicSource(
    LocalMusicPlayer player,
    MusicTrackStore tracks,
    MusicPlaylistStore playlists,
    MusicFileStorage files) : IMusicSource
{
    public string Key => "local";

    public bool IsAvailable => true;

    // Advertise local transport only when there is something it could control: a non-empty library, or a
    // queue already loaded (playing or paused). A fresh install with zero tracks shows no dead controls.
    public async Task<bool> IsAdvertisedAsync() => player.GetState() is not null || await tracks.AnyAsync();

    public async Task PlayAsync(string id)
    {
        if (!LocalMusicId.TryParse(id, out var kind, out var entityId))
            throw new ValidationException("error.music.unknownPlayId", id);

        if (kind == "track")
        {
            var track = await tracks.GetAsync(entityId)
                ?? throw new NotFoundException("error.notFound.musicTrack", entityId);
            player.Play(contextName: null, [ToItem(track)]);
        }
        else // playlist
        {
            var playlist = await playlists.GetAsync(entityId)
                ?? throw new NotFoundException("error.notFound.musicPlaylist", entityId);
            var items = new List<QueueItem>();
            foreach (var trackId in playlist.TrackIds)
                if (await tracks.GetAsync(trackId) is { } track)
                    items.Add(ToItem(track));
            if (items.Count == 0)
                throw new ValidationException("error.music.emptyPlaylist", playlist.Name);
            player.Play(playlist.Name, items);
        }
    }

    public Task PauseAsync(bool throwOnNoDevice = true) { player.Pause(); return Task.CompletedTask; }
    public Task ResumeAsync() { player.Resume(); return Task.CompletedTask; }
    public Task NextAsync() { player.Next(); return Task.CompletedTask; }
    public Task PreviousAsync() { player.Previous(); return Task.CompletedTask; }
    public Task SetVolumeAsync(double volume01) { player.SetVolume(volume01); return Task.CompletedTask; }
    public Task SetShuffleAsync(bool shuffle) { player.SetShuffle(shuffle); return Task.CompletedTask; }
    public Task SetRepeatAsync(string mode) { player.SetRepeat(mode); return Task.CompletedTask; }

    public Task<MusicState?> GetStateAsync()
    {
        var s = player.GetState();
        return Task.FromResult<MusicState?>(s is null
            ? null
            : new MusicState("local", s.IsPlaying, s.TrackName, s.ArtistName, s.ContextName,
                DeviceName: null, s.VolumePercent, s.ProgressSeconds, s.DurationSeconds, s.IsShuffling, s.Repeat));
    }

    private QueueItem ToItem(Models.MusicTrack track) =>
        new(track.Id, track.Name, track.Artist, files.FullPath(track), track.DurationMs);
}
