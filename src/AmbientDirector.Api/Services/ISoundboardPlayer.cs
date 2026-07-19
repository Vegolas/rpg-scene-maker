namespace AmbientDirector.Api.Services;

/// <summary>
/// The soundboard's playback surface — overlapping "voices" on the server's own audio device (this is what
/// Kenku FM used to do). The single implementation, <see cref="SoundboardPlayer"/>, is cross-platform: its
/// managed NAudio mixing graph runs everywhere and it takes the actual output device from an
/// <c>IWavePlayerFactory</c> (NAudio's <c>WaveOutEvent</c> on Windows, an OpenAL sink elsewhere — issue #82).
/// File decoding used at import time (duration + waveform) lives in the shared, platform-agnostic
/// <c>SoundDecoder</c>.
/// </summary>
public interface ISoundboardPlayer
{
    /// <summary>Ids of the sounds currently playing (deduped), for the panel's live highlight.</summary>
    IReadOnlyList<string> PlayingIds { get; }

    /// <summary>Start playing <paramref name="filePath"/>; overlaps anything already playing. Returns a
    /// per-voice handle for <see cref="StopVoice"/> (e.g. a timeline clip stopping just its own voice).
    /// Throws <see cref="SoundboardException"/> when playback is unavailable — no audio output device, or the
    /// host OS doesn't support the soundboard at all.</summary>
    Guid Play(string soundId, string filePath, bool loop, double volume);

    /// <summary>Stop every voice playing this sound id (there may be more than one when overlapping).</summary>
    void Stop(string soundId);

    /// <summary>Stop just the voice with this handle (no-op if it already finished on its own).</summary>
    void StopVoice(Guid handle);

    /// <summary>Stop all playback.</summary>
    void StopAll();
}
