namespace AmbientDirector.Api.Services;

/// <summary>
/// The soundboard on a non-Windows host: playback is <b>gracefully disabled</b> (issue #81). NAudio's
/// <c>WaveOutEvent</c> — the only Windows-bound API in the app — has no cross-platform equivalent here yet
/// (that's Phase 2, #82), so this stand-in is registered instead of <see cref="SoundboardPlayer"/> on
/// Linux/macOS. <see cref="Play"/> throws the same localized <see cref="SoundboardException"/> a missing
/// audio device would (→ 503 / a 207 on scene activation), and the stop/state members are harmless no-ops so
/// scenes, events and the reset paths still run. The panel's Sounds tab surfaces an explicit "unavailable on
/// this OS" banner (via <c>DiagnosticsDto.SoundboardSupported</c>) so a tap explains itself rather than only
/// toasting the 503.
/// </summary>
public sealed class NullSoundboardPlayer : ISoundboardPlayer
{
    public IReadOnlyList<string> PlayingIds => [];

    public Guid Play(string soundId, string filePath, bool loop, double volume) =>
        throw new SoundboardException(
            "The soundboard is only available on Windows; sound playback is disabled on this operating system.");

    public void Stop(string soundId) { }

    public void StopVoice(Guid handle) { }

    public void StopAll() { }
}
