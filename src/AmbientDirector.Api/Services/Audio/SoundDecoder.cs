using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer.NAudioSupport;

namespace AmbientDirector.Api.Services.Audio;

/// <summary>
/// Shared, <b>platform-agnostic</b> audio decoding for the whole app — the one place that turns a file on
/// disk into a <see cref="WaveStream"/> and brings any reader to the mixer/device format. Used by both
/// <see cref="SoundboardPlayer"/> and <c>LocalMusicPlayer</c> for playback, and by the import/list paths for
/// measuring a file's natural length + waveform preview.
///
/// <para>Everything here decodes on every OS (issue #82): WAV via NAudio's managed <c>WaveFileReader</c>
/// (behind <see cref="AudioFileReader"/>), OGG via NVorbis, and — crucially — <b>MP3 via the fully-managed
/// NLayer decoder</b> rather than <see cref="AudioFileReader"/>'s Windows-only ACM codec. Routing <c>.mp3</c>
/// to NLayer on <em>all</em> platforms (not just non-Windows) keeps one code path, so a Windows run exercises
/// the exact decode a Linux/macOS box uses.</para>
/// </summary>
public static class SoundDecoder
{
    /// <summary>The common mix/device format everything is resampled / up-mixed to (see <see cref="Normalize"/>).</summary>
    public const int SampleRate = 44100;
    public const int Channels = 2;

    /// <summary>Number of amplitude buckets in a stored waveform preview (one byte each, 0–255). Small
    /// enough to ride along in <c>SoundDto</c>, dense enough to draw a recognizable shape on a timeline clip.</summary>
    public const int WaveformBuckets = 120;

    /// <summary>Open <paramref name="path"/> as a <see cref="WaveStream"/>, picking the decoder by extension:
    /// OGG → NVorbis, MP3 → managed NLayer, everything else (WAV/AIFF) → <see cref="AudioFileReader"/>. All
    /// three are managed, so this works identically on Windows, Linux and macOS.</summary>
    public static WaveStream CreateReader(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
            return new VorbisWaveReader(path);
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            // Managed MP3 decode (no Windows ACM dependency). Mp3FileReaderBase drives NLayer's frame
            // decompressor, and still exposes TotalTime/CurrentTime/Position (seek) like any WaveStream —
            // so duration measurement and LoopStream's rewind keep working.
            var builder = new Mp3FileReaderBase.FrameDecompressorBuilder(wf => new Mp3FrameDecompressor(wf));
            return new Mp3FileReaderBase(path, builder);
        }
        return new AudioFileReader(path);
    }

    /// <summary>Bring any reader to the common <see cref="SampleRate"/>/<see cref="Channels"/> float format:
    /// resample if needed and up-mix mono to stereo. Throws <see cref="SoundboardException"/> for an
    /// unsupported channel count (only mono and stereo are handled).</summary>
    public static ISampleProvider Normalize(ISampleProvider source)
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
}
