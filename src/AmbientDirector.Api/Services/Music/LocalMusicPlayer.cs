using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Audio;

namespace AmbientDirector.Api.Services.Music;

/// <summary>One track's worth of queue metadata handed to the player (the file to decode plus what to show).</summary>
public record QueueItem(string Id, string Name, string Artist, string FilePath, int? DurationMs);

/// <summary>The local player's live state (mapped to the neutral <see cref="MusicState"/> by the source).</summary>
public record LocalPlaybackState(
    bool IsPlaying, string? TrackName, string? ArtistName, string? ContextName,
    int VolumePercent, double? ProgressSeconds, double? DurationSeconds, bool IsShuffling, string Repeat);

/// <summary>
/// Plays local music files on the server's own audio device — the sibling of <see cref="SoundboardPlayer"/>,
/// but for one continuous music stream rather than overlapping one-shots (its own <see cref="WaveOutEvent"/>,
/// not the soundboard mixer). A play builds a queue (a single track, or a playlist in order); shuffle and
/// repeat off/track/playlist are honoured at each track's natural end.
///
/// <para>Structured exactly like <see cref="SoundboardPlayer"/>, sharing its one place to swap the sink: the
/// player <em>is</em> the <see cref="ISampleProvider"/> feeding the device, always returning a full buffer
/// (silence when idle/paused) so the device stays open — advancing to the next track happens inside
/// <see cref="Read"/> on the audio thread, so there is no <c>PlaybackStopped</c> stop-vs-dispose race to get
/// wrong. Pause is simply "emit silence", never a device stop. All output-device creation lives in the one
/// lazy <see cref="EnsureOutputLocked"/> (first play only, never at construction/DI time), takes the device
/// from the injected <see cref="IWavePlayerFactory"/> (WaveOut on Windows, OpenAL elsewhere — issue #82), and
/// wraps failures in <see cref="SoundboardException"/>, so a host with no audio device degrades to a clean
/// localized 503 — never a crash. Decoding + normalization use the shared <see cref="SoundDecoder"/> (managed
/// WAV/OGG/MP3, cross-platform). Thread-safe via a single lock, like the soundboard.</para>
/// </summary>
public sealed class LocalMusicPlayer : ISampleProvider, IDisposable
{
    private readonly IWavePlayerFactory _playerFactory;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SoundDecoder.SampleRate, SoundDecoder.Channels);

    private readonly object _lock = new();
    private readonly Random _rng = new();

    public LocalMusicPlayer(IWavePlayerFactory playerFactory) => _playerFactory = playerFactory;

    private IWavePlayer? _output;
    private WaveStream? _readerStream;          // the current decoded file (disposed on track change)
    private VolumeSampleProvider? _volumeProvider; // wraps the normalized reader; volume tweaked live
    private ISampleProvider? _current;          // == _volumeProvider when a track is loaded, else null

    private List<QueueItem> _queue = [];        // the queue in its authored order
    private List<int> _order = [];              // playback order: indices into _queue (shuffled or 0..n-1)
    private int _orderPos = -1;                 // position within _order, or -1 when nothing is loaded
    private string? _contextName;               // playlist name (null for a single track)

    private bool _paused;
    private bool _shuffle;
    private string _repeat = "off";             // off | track | playlist
    private float _volume = 1f;
    private bool _disposed;

    /// <summary>Start playing a queue, replacing whatever was playing. A single track passes a one-item list
    /// and a null <paramref name="contextName"/>; a playlist passes its tracks in order and its name. Throws
    /// <see cref="SoundboardException"/> when the audio device is unavailable or none of the files can be
    /// opened.</summary>
    public void Play(string? contextName, IReadOnlyList<QueueItem> items)
    {
        lock (_lock)
        {
            EnsureOutputLocked();
            _queue = [.. items];
            _contextName = contextName;
            _orderPos = -1;
            _paused = false;
            BuildOrderLocked();

            // Open the first playable track; skip missing/undecodable ones, and fail loudly only if none open.
            if (!AdvanceLocked(natural: false))
            {
                StopPlaybackLocked();
                throw new SoundboardException(_queue.Count == 1
                    ? $"Could not play '{_queue[0].Name}' — the file is missing or unsupported."
                    : "None of the playlist's tracks could be played (missing or unsupported files).");
            }
        }
    }

    /// <summary>The id of the track currently loaded, or null when nothing is playing. Lets the library know
    /// whether deleting a track must first release its file (the current reader holds it open on Windows).</summary>
    public string? CurrentTrackId
    {
        get
        {
            lock (_lock)
                return _current is not null && _orderPos >= 0 ? _queue[_order[_orderPos]].Id : null;
        }
    }

    /// <summary>Stop playback entirely and release the current file (disposes the reader). Used when the
    /// playing track is being deleted from the library.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            StopPlaybackLocked();
            _paused = false;
        }
    }

    /// <summary>Pause playback (the device keeps running, emitting silence). A no-op if nothing is loaded.</summary>
    public void Pause()
    {
        lock (_lock) _paused = true;
    }

    /// <summary>Resume paused playback. A no-op when nothing is loaded.</summary>
    public void Resume()
    {
        lock (_lock)
            if (_current is not null) _paused = false;
    }

    /// <summary>Skip to the next track. At the end of the queue it wraps when repeating the playlist, otherwise
    /// it stops. A no-op when nothing is loaded.</summary>
    public void Next()
    {
        lock (_lock)
        {
            if (_current is null) return;
            if (AdvanceLocked(natural: false)) _paused = false;
            else StopPlaybackLocked();
        }
    }

    /// <summary>Go to the previous track (restarts the first track rather than going nowhere; wraps to the end
    /// when repeating the playlist). A no-op when nothing is loaded.</summary>
    public void Previous()
    {
        lock (_lock)
        {
            if (_current is null || _order.Count == 0) return;
            var pos = _orderPos - 1;
            for (var attempts = 0; attempts <= _queue.Count; attempts++)
            {
                if (pos < 0) pos = _repeat == "playlist" ? _order.Count - 1 : 0;
                if (OpenOrderPosLocked(pos)) { _paused = false; return; }
                pos--;
            }
            StopPlaybackLocked();
        }
    }

    /// <summary>Set the output volume live, 0.0–1.0.</summary>
    public void SetVolume(double volume01)
    {
        lock (_lock)
        {
            _volume = (float)Math.Clamp(volume01, 0.0, 1.0);
            if (_volumeProvider is not null) _volumeProvider.Volume = _volume;
        }
    }

    /// <summary>Turn shuffle on/off, reshuffling (or restoring) the not-yet-played remainder while the current
    /// track keeps playing in place.</summary>
    public void SetShuffle(bool shuffle)
    {
        lock (_lock)
        {
            if (_shuffle == shuffle) return;
            _shuffle = shuffle;
            if (_queue.Count > 0) RebuildOrderKeepingCurrentLocked();
        }
    }

    /// <summary>Set the repeat mode: off | track | playlist (anything else is treated as off).</summary>
    public void SetRepeat(string mode)
    {
        lock (_lock) _repeat = mode is "off" or "track" or "playlist" ? mode : "off";
    }

    /// <summary>Current playback state, or null when nothing is loaded/playing.</summary>
    public LocalPlaybackState? GetState()
    {
        lock (_lock)
        {
            if (_output is null || _current is null || _orderPos < 0 || _queue.Count == 0) return null;
            var item = _queue[_order[_orderPos]];
            double? durationSec = item.DurationMs is int d and > 0 ? d / 1000.0 : null;
            double? progressSec = null;
            try { progressSec = _readerStream?.CurrentTime.TotalSeconds; } catch { /* some readers can't report position */ }
            return new LocalPlaybackState(
                IsPlaying: !_paused,
                TrackName: item.Name,
                ArtistName: string.IsNullOrWhiteSpace(item.Artist) ? null : item.Artist,
                ContextName: _contextName,
                VolumePercent: (int)Math.Round(_volume * 100),
                ProgressSeconds: progressSec,
                DurationSeconds: durationSec,
                IsShuffling: _shuffle,
                Repeat: _repeat);
        }
    }

    // ---- audio thread ----

    // Produce exactly `count` samples: play the current track, advancing to the next on natural end, and pad
    // any remainder with silence (so the device never underruns and stays open, like the soundboard mixer's
    // ReadFully). Runs on NAudio's playback thread; the lock serialises it against the control methods.
    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            var read = 0;
            var advances = 0;
            while (read < count && !_paused && _current is not null)
            {
                var n = _current.Read(buffer, offset + read, count - read);
                if (n > 0)
                {
                    read += n;
                    continue;
                }
                // The current track produced nothing more — it ended. Advance (honouring repeat/shuffle). The
                // guard bounds advances per call so a queue of empty/undecodable tracks can't spin forever.
                if (++advances > _queue.Count + 1 || !AdvanceLocked(natural: true))
                {
                    StopPlaybackLocked();
                    break;
                }
            }
            Array.Clear(buffer, offset + read, count - read);
            return count;
        }
    }

    // ---- queue navigation (caller holds _lock) ----

    // Move to the next thing to play. natural=true is a track ending on its own (honours repeat-one);
    // natural=false is a manual Next (always moves to a different track). Returns false when there's nothing
    // more to play (repeat off and past the end, or the whole queue is unopenable). Skips files that won't open.
    private bool AdvanceLocked(bool natural)
    {
        if (_queue.Count == 0) return false;

        if (natural && _repeat == "track" && _orderPos >= 0)
            return OpenOrderPosLocked(_orderPos); // replay the same track

        var pos = _orderPos;
        for (var attempts = 0; attempts <= _queue.Count; attempts++)
        {
            pos++;
            if (pos >= _order.Count)
            {
                if (_repeat != "playlist") return false; // "off": stop at the end
                if (_shuffle) ReshuffleLocked();         // fresh shuffle each time the playlist wraps
                pos = 0;
            }
            if (OpenOrderPosLocked(pos)) return true;
        }
        return false; // nothing in the queue could be opened
    }

    // Open the track at order position `pos`, replacing the current reader. Returns false (leaving the current
    // track untouched) when the file is missing or won't decode, so callers can skip to the next candidate.
    private bool OpenOrderPosLocked(int pos)
    {
        if (pos < 0 || pos >= _order.Count) return false;
        var item = _queue[_order[pos]];
        WaveStream? reader = null;
        try
        {
            reader = SoundDecoder.CreateReader(item.FilePath);
            var volume = new VolumeSampleProvider(SoundDecoder.Normalize(reader.ToSampleProvider())) { Volume = _volume };
            DisposeReaderLocked();
            _readerStream = reader;
            _volumeProvider = volume;
            _current = volume;
            _orderPos = pos;
            return true;
        }
        catch
        {
            reader?.Dispose();
            return false;
        }
    }

    private void BuildOrderLocked()
    {
        _order = [.. Enumerable.Range(0, _queue.Count)];
        if (_shuffle) Shuffle(_order);
    }

    private void ReshuffleLocked()
    {
        _order = [.. Enumerable.Range(0, _queue.Count)];
        Shuffle(_order);
    }

    // Keep the already-played prefix (including the current track) fixed and re-order only the remainder:
    // a fresh shuffle when turning shuffle on, or ascending queue order when turning it off.
    private void RebuildOrderKeepingCurrentLocked()
    {
        var played = _orderPos >= 0 ? _order.Take(_orderPos + 1).ToList() : [];
        var playedSet = played.ToHashSet();
        var remaining = Enumerable.Range(0, _queue.Count).Where(i => !playedSet.Contains(i)).ToList();
        if (_shuffle) Shuffle(remaining);
        _order = [.. played, .. remaining];
    }

    private void Shuffle(List<int> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void StopPlaybackLocked()
    {
        DisposeReaderLocked();
        _current = null;
        _volumeProvider = null;
        _orderPos = -1;
    }

    private void DisposeReaderLocked()
    {
        _readerStream?.Dispose();
        _readerStream = null;
    }

    // The one place an output device is created — lazily, on first play, wrapped so a machine with no audio
    // device (or a non-Windows host) surfaces a clean SoundboardException (→ localized 503) instead of crashing.
    private void EnsureOutputLocked()
    {
        if (_disposed) throw new SoundboardException("The music player has been shut down.");
        if (_output is not null) return;
        try
        {
            var output = _playerFactory.Create(200);
            output.Init(this); // this player is the sample provider (silence until a track is loaded)
            output.Play();
            _output = output;
        }
        catch (Exception ex) when (ex is not SoundboardException)
        {
            throw new SoundboardException($"No audio output device is available on the server: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _output?.Dispose();
            _output = null;
            DisposeReaderLocked();
            _current = null;
            _volumeProvider = null;
        }
    }
}
