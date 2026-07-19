namespace AmbientDirector.Api.Services;

/// <summary>
/// The soundboard's playback surface — overlapping "voices" on the server's own audio device (this is what
/// Kenku FM used to do). Two implementations, chosen per-OS in Program.cs: the Windows one
/// (<see cref="SoundboardPlayer"/>) drives NAudio's <c>WaveOutEvent</c>; on Linux/macOS
/// <see cref="NullSoundboardPlayer"/> stands in and playback is <b>gracefully disabled</b> (issue #81) — its
/// <see cref="Play"/> throws the localized <see cref="SoundboardException"/> and the panel's Sounds tab shows
/// an "unavailable on this OS" banner. The static file-decode helpers used at import time (duration +
/// waveform) stay on the concrete <see cref="SoundboardPlayer"/>: they're platform-agnostic and called by
/// type name, not injected, so they keep working everywhere.
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
