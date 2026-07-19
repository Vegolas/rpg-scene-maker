using Silk.NET.OpenAL;

namespace AmbientDirector.Api.Services.Audio;

/// <summary>
/// The process-shared OpenAL device + context that every <see cref="OpenAlWavePlayer"/> renders into. Created
/// lazily by <see cref="OpenAlPlayerFactory"/> on the first sound played, and disposed at app shutdown.
///
/// <para><b>Why shared:</b> OpenAL's <em>current context</em> is a process-wide property (there is one current
/// context, not one per thread). The app runs two independent players — the soundboard and the local-music
/// player — each on its own pump thread; if each opened its own context and called
/// <c>MakeContextCurrent</c>, they would clobber one another. So we open <b>one</b> device + context, make it
/// current <b>exactly once</b> here, and never change it again. Both players create their own source(s) in this
/// one context; concurrent AL calls from their pump threads are then safe (OpenAL Soft locks internally).</para>
///
/// <para><see cref="AL.GetApi"/>/<see cref="ALContext.GetApi"/> are called with <c>soft: true</c> so Silk.NET
/// loads the bundled <c>Silk.NET.OpenAL.Soft.Native</c> (OpenAL Soft) rather than searching for a
/// system-installed OpenAL. Any failure to open the device/context is wrapped in
/// <see cref="SoundboardException"/> → a clean localized 503, mirroring the "no audio device" behaviour of the
/// Windows sink.</para>
/// </summary>
public sealed unsafe class OpenAlContext : IDisposable
{
    /// <summary>The core AL entry points (source/buffer calls). Shared by all players in this process.</summary>
    public AL Al { get; }

    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;

    public OpenAlContext()
    {
        try
        {
            _alc = ALContext.GetApi(soft: true);
            Al = AL.GetApi(soft: true);
        }
        catch (Exception ex)
        {
            throw new SoundboardException($"OpenAL is not available on this server: {ex.Message}", ex);
        }

        _device = _alc.OpenDevice("");
        if (_device is null)
            throw new SoundboardException(
                "No audio output device is available on the server (OpenAL could not open the default device).");

        _context = _alc.CreateContext(_device, null);
        if (_context is null)
        {
            _alc.CloseDevice(_device);
            throw new SoundboardException(
                "The server's audio device could not be initialized (OpenAL context creation failed).");
        }

        if (!_alc.MakeContextCurrent(_context))
        {
            _alc.DestroyContext(_context);
            _alc.CloseDevice(_device);
            throw new SoundboardException(
                "The server's audio context could not be activated (OpenAL MakeContextCurrent failed).");
        }
    }

    public void Dispose()
    {
        _alc.MakeContextCurrent((Context*)null);
        if (_context is not null) _alc.DestroyContext(_context);
        if (_device is not null) _alc.CloseDevice(_device);
        Al.Dispose();
        _alc.Dispose();
    }
}
