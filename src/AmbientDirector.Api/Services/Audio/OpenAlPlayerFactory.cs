using NAudio.Wave;

namespace AmbientDirector.Api.Services.Audio;

/// <summary>
/// The cross-platform audio sink factory (issue #82): hands out <see cref="OpenAlWavePlayer"/>s that all share
/// one process-wide <see cref="OpenAlContext"/>. Registered off-Windows (or when <c>Audio:Backend=openal</c>).
///
/// <para>The device/context is opened <b>lazily</b> on the first <see cref="Create"/> (i.e. the first sound
/// played), never at startup — so a server that never plays audio never touches the audio hardware, and a host
/// with no output device surfaces a clean <see cref="SoundboardException"/> (→ localized 503) at play time
/// instead of crashing at boot. The failure is raised inside the player's own start path (which is already
/// wrapped in a <c>try</c> → 503), because <see cref="Create"/> is called from there.</para>
/// </summary>
public sealed class OpenAlPlayerFactory : IWavePlayerFactory, IDisposable
{
    private readonly object _lock = new();
    private OpenAlContext? _context;
    private bool _disposed;

    public IWavePlayer Create(int desiredLatencyMs)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _context ??= new OpenAlContext();
            return new OpenAlWavePlayer(_context, desiredLatencyMs);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _context?.Dispose();
            _context = null;
        }
    }
}
