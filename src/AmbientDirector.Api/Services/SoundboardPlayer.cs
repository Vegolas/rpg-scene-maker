using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Services;

/// <summary>
/// Plays sound effects on the server's own audio device (this is what Kenku FM used to do).
/// A single output device is fed by a <see cref="MixingSampleProvider"/> so any number of sounds can
/// overlap (polyphony); each playing sound is one "voice" with its own volume and optional looping.
/// One-shots clean themselves up when they finish; loops run until stopped.
/// </summary>
public sealed class SoundboardPlayer : IDisposable
{
    // Everything is mixed to this common format; per-sound readers are resampled / up-mixed to match.
    private const int SampleRate = 44100;
    private const int Channels = 2;

    /// <summary>Number of amplitude buckets in a stored waveform preview (one byte each, 0–255). Small
    /// enough to ride along in <c>SoundDto</c>, dense enough to draw a recognizable shape on a timeline clip.</summary>
    public const int WaveformBuckets = 120;

    private readonly object _lock = new();
    private readonly List<Voice> _voices = [];
    private IWavePlayer? _output;
    private MixingSampleProvider? _mixer;

    /// <summary>Ids of the sounds currently playing (deduped), for the panel's live highlight.</summary>
    public IReadOnlyList<string> PlayingIds
    {
        get
        {
            lock (_lock)
                return _voices.Select(v => v.SoundId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Start playing <paramref name="filePath"/>; overlaps anything already playing. Returns a
    /// per-voice handle for <see cref="StopVoice"/> (e.g. a timeline clip stopping just its own voice).</summary>
    public Guid Play(string soundId, string filePath, bool loop, double volume)
    {
        if (!File.Exists(filePath))
            throw new SoundboardException($"Sound file is missing: {Path.GetFileName(filePath)}");

        lock (_lock)
        {
            EnsureStarted();

            WaveStream source;
            try
            {
                var reader = CreateReader(filePath);
                source = loop ? new LoopStream(reader) : reader;
            }
            catch (Exception ex) when (ex is not SoundboardException)
            {
                throw new SoundboardException($"Could not decode '{Path.GetFileName(filePath)}': {ex.Message}", ex);
            }

            ISampleProvider chain;
            try
            {
                chain = new VolumeSampleProvider(Normalize(source.ToSampleProvider()))
                {
                    Volume = (float)Math.Clamp(volume, 0.0, 1.0),
                };
            }
            catch
            {
                source.Dispose();
                throw;
            }

            var handle = Guid.NewGuid();
            _mixer!.AddMixerInput(chain);
            _voices.Add(new Voice(handle, soundId, chain, source));
            return handle;
        }
    }

    /// <summary>Stop every voice playing this sound id (there may be more than one when overlapping).</summary>
    public void Stop(string soundId)
    {
        lock (_lock)
        {
            foreach (var voice in _voices.Where(v => v.SoundId.Equals(soundId, StringComparison.OrdinalIgnoreCase)).ToList())
                Remove(voice);
        }
    }

    /// <summary>Stop just the voice with this handle (no-op if it already finished on its own).</summary>
    public void StopVoice(Guid handle)
    {
        lock (_lock)
        {
            if (_voices.FirstOrDefault(v => v.Handle == handle) is { } voice)
                Remove(voice);
        }
    }

    /// <summary>Stop all playback.</summary>
    public void StopAll()
    {
        lock (_lock)
        {
            _mixer?.RemoveAllMixerInputs();
            foreach (var voice in _voices)
                voice.Source.Dispose();
            _voices.Clear();
        }
    }

    // Lazily open the output device so a machine that never plays a sound (or has no audio device)
    // doesn't fail at startup — only a play attempt surfaces the problem.
    private void EnsureStarted()
    {
        if (_output is not null) return;
        try
        {
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
            {
                // Keep producing (silence when idle) so the device stays open and later sounds start instantly.
                ReadFully = true,
            };
            mixer.MixerInputEnded += OnMixerInputEnded;
            var output = new WaveOutEvent { DesiredLatency = 150 };
            output.Init(mixer);
            output.Play();
            _mixer = mixer;
            _output = output;
        }
        catch (Exception ex)
        {
            throw new SoundboardException($"No audio output device is available on the server: {ex.Message}", ex);
        }
    }

    // One-shots finish on the audio thread: drop and dispose the voice. (Manual Stop/StopAll removals
    // do not raise this event, so there's no double-dispose.)
    private void OnMixerInputEnded(object? sender, SampleProviderEventArgs e)
    {
        lock (_lock)
        {
            var voice = _voices.FirstOrDefault(v => ReferenceEquals(v.Root, e.SampleProvider));
            if (voice is not null)
            {
                _voices.Remove(voice);
                voice.Source.Dispose();
            }
        }
    }

    // Caller holds _lock.
    private void Remove(Voice voice)
    {
        _mixer?.RemoveMixerInput(voice.Root);
        _voices.Remove(voice);
        voice.Source.Dispose();
    }

    private static WaveStream CreateReader(string path) =>
        Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(path)
            : new AudioFileReader(path);

    /// <summary>Measure a file's natural length using the same reader logic as playback. Returns null when
    /// the file is missing or can't be decoded (callers persist/skip accordingly, never failing on it).</summary>
    public static int? TryMeasureDurationMs(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            using var reader = CreateReader(filePath);
            return (int)reader.TotalTime.TotalMilliseconds;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Compute a compact amplitude preview (<see cref="WaveformBuckets"/> peaks, each 0–255 and
    /// normalized so the loudest sample is full-scale) for the timeline editor's waveform display, using the
    /// same reader as playback. Streams the file in one pass (bucketing on the fly, never buffering the whole
    /// decoded audio). Returns null when the file is missing or won't decode — callers persist the empty-array
    /// "tried, unmeasurable" sentinel so the backfill doesn't re-probe it.</summary>
    public static byte[]? TryComputeWaveform(string filePath, int buckets = WaveformBuckets)
    {
        if (!File.Exists(filePath) || buckets < 1) return null;
        try
        {
            using var reader = CreateReader(filePath);
            var samples = reader.ToSampleProvider();
            var channels = Math.Max(1, samples.WaveFormat.Channels);

            // Size the buckets from the reported length so each frame lands in the right one in a single pass;
            // a slightly-off total just over/underfills the final bucket, which is imperceptible at this scale.
            var totalFrames = (long)(reader.TotalTime.TotalSeconds * samples.WaveFormat.SampleRate);
            var framesPerBucket = Math.Max(1, totalFrames / buckets);

            var peaks = new float[buckets];
            var buffer = new float[samples.WaveFormat.SampleRate * channels]; // ~1s chunks
            var maxPeak = 0f;
            long frame = 0;
            int read;
            while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i + channels <= read; i += channels)
                {
                    // Peak (max abs) across this frame's channels.
                    var amp = 0f;
                    for (var c = 0; c < channels; c++)
                        amp = Math.Max(amp, Math.Abs(buffer[i + c]));

                    var bucket = (int)Math.Min(buckets - 1, frame / framesPerBucket);
                    if (amp > peaks[bucket]) peaks[bucket] = amp;
                    if (amp > maxPeak) maxPeak = amp;
                    frame++;
                }
            }

            var result = new byte[buckets];
            if (maxPeak > 0)
                for (var i = 0; i < buckets; i++)
                    result[i] = (byte)Math.Clamp((int)Math.Round(peaks[i] / maxPeak * 255f), 0, 255);
            return result;
        }
        catch
        {
            return null;
        }
    }

    // Bring any reader to the mixer's 44.1 kHz / stereo float format.
    private static ISampleProvider Normalize(ISampleProvider source)
    {
        if (source.WaveFormat.SampleRate != SampleRate)
            source = new WdlResamplingSampleProvider(source, SampleRate);
        return source.WaveFormat.Channels switch
        {
            2 => source,
            1 => new MonoToStereoSampleProvider(source),
            var n => throw new SoundboardException($"Unsupported channel count ({n}); only mono and stereo are supported."),
        };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _output?.Dispose();
            foreach (var voice in _voices)
                voice.Source.Dispose();
            _voices.Clear();
            _output = null;
            _mixer = null;
        }
    }

    // Handle identifies this specific voice (StopVoice targets it); Root is the provider handed to the
    // mixer (used to remove it / match the ended event); Source is the underlying stream to dispose
    // (a LoopStream disposes its inner reader).
    private sealed record Voice(Guid Handle, string SoundId, ISampleProvider Root, WaveStream Source);
}

/// <summary>A <see cref="WaveStream"/> that restarts from the beginning when it reaches the end.</summary>
internal sealed class LoopStream(WaveStream source) : WaveStream
{
    public override WaveFormat WaveFormat => source.WaveFormat;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = source.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (source.Position == 0) break; // empty source — avoid a tight spin
                source.Position = 0;
            }
            total += read;
        }
        return total;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Thrown when a sound can't be played (no audio device, missing file, undecodable format).</summary>
public class SoundboardException(string message, Exception? inner = null) : Exception(message, inner);
