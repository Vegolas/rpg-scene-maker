using System.Runtime.InteropServices;
using NAudio.Wave;
using Silk.NET.OpenAL;

namespace AmbientDirector.Api.Services.Audio;

/// <summary>
/// A cross-platform audio output sink that implements NAudio's <see cref="IWavePlayer"/> on top of OpenAL, so
/// <see cref="SoundboardPlayer"/> and <c>LocalMusicPlayer</c> can keep their entire managed NAudio mixing graph
/// and only swap the device (issue #82). The player owns <b>one OpenAL source</b> fed by a small rotating queue
/// of buffers: a background <b>pump thread</b> reads float frames from the NAudio provider handed to
/// <see cref="Init"/>, converts them to 16-bit PCM, and keeps the queue topped up.
///
/// <para>All OpenAL objects live in the shared <see cref="OpenAlContext"/> (see its docs for why the context is
/// process-wide and set current once). This player never touches the context's current-ness; it only creates
/// its own source/buffers and issues source/buffer calls, which are safe to run concurrently with the other
/// player's pump thread.</para>
///
/// <para>Startup (device open + source/buffer creation + first fill + <c>SourcePlay</c>) happens synchronously
/// inside <see cref="Play"/>, on the caller's thread — which for both players sits inside a
/// <c>try</c>/<see cref="SoundboardException"/> block, so a host with no audio device still degrades to a clean
/// localized 503 rather than failing on a background thread. Only the ongoing queue maintenance runs on the
/// pump thread.</para>
/// </summary>
public sealed class OpenAlWavePlayer : IWavePlayer
{
    // A handful of small buffers: enough to ride over scheduling jitter without adding much latency. The
    // per-buffer size is derived from the caller's desired latency (see the ctor).
    private const int BufferCount = 4;

    private readonly OpenAlContext _context;
    private readonly AL _al;
    private readonly int _desiredLatencyMs;
    private readonly object _lock = new();

    private IWaveProvider? _provider;      // the float source (SampleToWaveProvider over the mixer / music player)
    private WaveFormat _outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private BufferFormat _alFormat = BufferFormat.Stereo16;
    private int _sampleRate = 44100;
    private int _channels = 2;

    private byte[] _readBuffer = [];       // raw float bytes read from the provider (one buffer's worth)
    private short[] _pcm = [];             // converted 16-bit PCM handed to OpenAL
    private readonly uint[] _one = new uint[1]; // reused for single unqueue/requeue

    private uint _source;
    private uint[] _buffers = [];
    private Thread? _pump;
    private volatile bool _running;
    private bool _started;
    private bool _disposed;
    private float _volume = 1f;
    private PlaybackState _state = PlaybackState.Stopped;

    public OpenAlWavePlayer(OpenAlContext context, int desiredLatencyMs)
    {
        _context = context;
        _al = context.Al;
        _desiredLatencyMs = Math.Max(20, desiredLatencyMs);
    }

    public PlaybackState PlaybackState { get { lock (_lock) return _state; } }

    public WaveFormat OutputWaveFormat { get { lock (_lock) return _outputFormat; } }

    public float Volume
    {
        get { lock (_lock) return _volume; }
        set { lock (_lock) _volume = Math.Clamp(value, 0f, 1f); }
    }

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public void Init(IWaveProvider waveProvider)
    {
        lock (_lock)
        {
            _provider = waveProvider;
            _outputFormat = waveProvider.WaveFormat;
            _channels = _outputFormat.Channels;
            _sampleRate = _outputFormat.SampleRate;

            // Callers reach Init via NAudio's Init(this IWavePlayer, ISampleProvider) extension, which wraps the
            // mixer in a SampleToWaveProvider → 32-bit IEEE float. We only support that (mono/stereo).
            if (_outputFormat.Encoding != WaveFormatEncoding.IeeeFloat || _outputFormat.BitsPerSample != 32)
                throw new SoundboardException(
                    $"OpenAL sink expects 32-bit float audio; got {_outputFormat.Encoding} {_outputFormat.BitsPerSample}-bit.");
            _alFormat = _channels switch
            {
                2 => BufferFormat.Stereo16,
                1 => BufferFormat.Mono16,
                var n => throw new SoundboardException($"Unsupported channel count ({n}); only mono and stereo are supported."),
            };

            // Size each buffer to ~ (desired latency / buffer count), floored so the pump isn't churning tiny
            // buffers. framesPerBuffer frames → readBuffer bytes of float in, _pcm shorts out.
            var framesPerBuffer = Math.Max(1024, _sampleRate * _desiredLatencyMs / 1000 / BufferCount);
            _readBuffer = new byte[framesPerBuffer * _outputFormat.BlockAlign];
            _pcm = new short[framesPerBuffer * _channels];
        }
    }

    public void Play()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_provider is null)
                throw new SoundboardException("OpenAL sink was not initialized before playback.");

            if (!_started)
            {
                _source = _al.GenSource();
                _buffers = _al.GenBuffers(BufferCount);
                foreach (var buffer in _buffers)
                    FillBuffer(buffer);
                _al.SourceQueueBuffers(_source, _buffers);
                ThrowOnAlError("starting OpenAL playback");

                _running = true;
                _pump = new Thread(PumpLoop) { IsBackground = true, Name = "openal-pump" };
                _pump.Start();
                _started = true;
            }

            _al.SourcePlay(_source);
            _state = PlaybackState.Playing;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_state != PlaybackState.Playing) return;
            _al.SourcePause(_source);
            _state = PlaybackState.Paused;
        }
    }

    public void Stop()
    {
        StopInternal(raiseEvent: true);
    }

    // ---- pump thread ----

    private void PumpLoop()
    {
        while (_running)
        {
            _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out var processed);
            if (processed <= 0)
            {
                // Recover from an underrun (source drained + stopped while data is still queued): re-issue Play.
                // Never re-start while the caller has paused us.
                lock (_lock)
                {
                    if (_running && _state == PlaybackState.Playing)
                    {
                        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
                        if (state != (int)SourceState.Playing)
                        {
                            _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out var queued);
                            if (queued > 0) _al.SourcePlay(_source);
                        }
                    }
                }
                Thread.Sleep(5);
                continue;
            }

            while (processed-- > 0 && _running)
            {
                lock (_lock)
                {
                    if (!_running) break;
                    _al.SourceUnqueueBuffers(_source, _one); // _one[0] = the freed buffer id
                    FillBuffer(_one[0]);
                    _al.SourceQueueBuffers(_source, _one);   // requeue the same buffer
                }
            }
        }
    }

    // Read one buffer's worth of float frames from the provider (padding with silence on a short read — our
    // sources always return a full buffer, so this is just belt-and-braces), convert to 16-bit PCM applying the
    // master volume, and upload to the given OpenAL buffer. Caller holds _lock (except the priming pass in
    // Play, which runs before the pump thread exists).
    private void FillBuffer(uint buffer)
    {
        var total = 0;
        while (total < _readBuffer.Length)
        {
            var n = _provider!.Read(_readBuffer, total, _readBuffer.Length - total);
            if (n == 0)
            {
                Array.Clear(_readBuffer, total, _readBuffer.Length - total);
                break;
            }
            total += n;
        }

        var floats = MemoryMarshal.Cast<byte, float>(_readBuffer.AsSpan());
        var vol = _volume;
        var count = Math.Min(floats.Length, _pcm.Length);
        for (var i = 0; i < count; i++)
        {
            var s = Math.Clamp(floats[i] * vol, -1f, 1f);
            _pcm[i] = (short)(s * short.MaxValue);
        }

        _al.BufferData(buffer, _alFormat, _pcm, _sampleRate);
    }

    private void StopInternal(bool raiseEvent)
    {
        lock (_lock)
        {
            if (!_started)
            {
                _state = PlaybackState.Stopped;
            }
            else
            {
                _running = false;
            }
        }

        // Join outside the lock so the pump thread (which takes _lock) can observe _running == false and exit.
        JoinPump();

        lock (_lock)
        {
            if (_started) _al.SourceStop(_source);
            _state = PlaybackState.Stopped;
        }

        if (raiseEvent) PlaybackStopped?.Invoke(this, new StoppedEventArgs());
    }

    private void JoinPump()
    {
        var pump = _pump;
        if (pump is not null && pump.IsAlive && pump != Thread.CurrentThread)
            pump.Join();
    }

    private void ThrowOnAlError(string what)
    {
        var err = _al.GetError();
        if (err != AudioError.NoError)
            throw new SoundboardException($"OpenAL error while {what}: {err}.");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
        }

        JoinPump();

        lock (_lock)
        {
            if (_started)
            {
                _al.SourceStop(_source);
                _al.DeleteSource(_source);
                if (_buffers.Length > 0) _al.DeleteBuffers(_buffers);
                _buffers = [];
                _started = false;
            }
            _state = PlaybackState.Stopped;
            _provider = null;
        }
    }
}
