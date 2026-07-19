using NAudio.Wave;

namespace AmbientDirector.Api.Services.Audio;

/// <summary>
/// The Windows audio sink: NAudio's <see cref="WaveOutEvent"/> (winmm). This is the default backend on
/// Windows — the long-proven playback path — and stays untouched by the cross-platform work (#82); the
/// OpenAL sink (<see cref="OpenAlPlayerFactory"/>) only steps in off-Windows or when explicitly selected
/// via <c>Audio:Backend</c>.
///
/// <para>Effectively Windows-only (<see cref="WaveOutEvent"/> P/Invokes <c>winmm</c>), but not annotated
/// <c>[SupportedOSPlatform("windows")]</c>: <c>Audio:Backend=waveout</c> may deliberately select it on any OS,
/// in which case a play attempt fails at the device open and is reported as the usual localized 503.</para>
/// </summary>
public sealed class WaveOutPlayerFactory : IWavePlayerFactory
{
    public IWavePlayer Create(int desiredLatencyMs) => new WaveOutEvent { DesiredLatency = desiredLatencyMs };
}
