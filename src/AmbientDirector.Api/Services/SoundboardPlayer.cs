using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using AmbientDirector.Api.Services.Audio;

namespace AmbientDirector.Api.Services;

/// <summary>
/// The soundboard: plays sound effects on the server's own audio device (this is what Kenku FM used to do). A
/// single output device is fed by a <see cref="MixingSampleProvider"/> so any number of sounds can overlap
/// (polyphony); each playing sound is one "voice" with its own volume and optional looping. One-shots clean
/// themselves up when they finish; loops run until stopped.
///
/// <para>The whole mixing graph is managed NAudio and runs on every OS; the only platform-specific piece is the
/// output device, which comes from the injected <see cref="IWavePlayerFactory"/> — NAudio's
/// <c>WaveOutEvent</c> on Windows, the cross-platform OpenAL sink elsewhere (issue #82). File decoding
/// (including duration + waveform measurement) lives in the shared <see cref="SoundDecoder"/>.</para>
/// </summary>
public sealed class SoundboardPlayer : ISoundboardPlayer, IDisposable
{
    private readonly IWavePlayerFactory _playerFactory;

    private readonly object _lock = new();
    private readonly List<Voice> _voices = [];
    private IWavePlayer? _output;
    private MixingSampleProvider? _mixer;

    public SoundboardPlayer(IWavePlayerFactory playerFactory) => _playerFactory = playerFactory;

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
                var reader = SoundDecoder.CreateReader(filePath);
                source = loop ? new LoopStream(reader) : reader;
            }
            catch (Exception ex) when (ex is not SoundboardException)
            {
                throw new SoundboardException($"Could not decode '{Path.GetFileName(filePath)}': {ex.Message}", ex);
            }

            ISampleProvider chain;
            try
            {
                chain = new VolumeSampleProvider(SoundDecoder.Normalize(source.ToSampleProvider()))
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
            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SoundDecoder.SampleRate, SoundDecoder.Channels))
            {
                // Keep producing (silence when idle) so the device stays open and later sounds start instantly.
                ReadFully = true,
            };
            mixer.MixerInputEnded += OnMixerInputEnded;
            var output = _playerFactory.Create(150);
            output.Init(mixer);
            output.Play();
            _mixer = mixer;
            _output = output;
        }
        catch (Exception ex) when (ex is not SoundboardException)
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
