using NAudio.Wave;

namespace AmbientDirector.Api.Services.Audio;

/// <summary>
/// Creates the audio output sink (NAudio's <see cref="IWavePlayer"/>) that a player pushes its mix into.
/// This is the one seam that varies by platform (issue #82): both <see cref="SoundboardPlayer"/> and
/// <c>LocalMusicPlayer</c> keep their managed NAudio mixing graph unchanged and only swap the sink through
/// this factory. Program.cs registers one implementation from the <c>Audio:Backend</c> config —
/// <see cref="WaveOutPlayerFactory"/> (NAudio <c>WaveOutEvent</c>, Windows-only) by default on Windows,
/// <see cref="OpenAlPlayerFactory"/> (cross-platform OpenAL) elsewhere.
/// </summary>
public interface IWavePlayerFactory
{
    /// <summary>Create a fresh, unstarted output device. The caller then <c>Init</c>s it with its sample
    /// source and calls <c>Play</c>. <paramref name="desiredLatencyMs"/> is the target buffering latency.</summary>
    IWavePlayer Create(int desiredLatencyMs);
}
