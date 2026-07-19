using System.Reflection;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Services;

namespace AmbientDirector.Api.Endpoints;

/// <summary>
/// Read-only runtime facts for the panel's developer mode (the Logs tab leads with a Diagnostics
/// panel fed by this). GET only — a pure read, not a command, so no <c>GetOrPost</c>; the optional
/// API key guards it via <c>IsProtectedPath</c> in Program.cs, like the other data routes.
/// </summary>
public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        app.MapGet("/diagnostics", async (
            IHostEnvironment env,
            DiagnosticsInfo info,
            SettingsStore settings,
            SpotifyStore spotify,
            ISoundboardPlayer soundboard,
            SceneStore scenes,
            SoundStore sounds,
            EventStore events) =>
        {
            var sceneCount = (await scenes.GetAllAsync()).Count;
            var soundCount = (await sounds.GetAllAsync()).Count;
            var eventCount = (await events.GetAllAsync()).Count;

            return new DiagnosticsDto(
                Version: AppVersion(),
                Environment: env.EnvironmentName,
                StartedAt: info.StartedAt,
                LightProvider: settings.Current.Provider,
                SpotifyConnected: spotify.Current.IsConnected,
                // Cross-platform since #82 (WaveOut on Windows, OpenAL elsewhere) — supported on every OS now.
                SoundboardSupported: true,
                PlayingSoundCount: soundboard.PlayingIds.Count,
                SceneCount: sceneCount,
                SoundCount: soundCount,
                EventCount: eventCount,
                DatabasePath: info.DatabasePath,
                SoundsPath: info.SoundsPath);
        });
    }

    private static string AppVersion()
    {
        var asm = typeof(Program).Assembly;
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString()
               ?? "unknown";
    }
}

/// <summary>Startup-captured facts the endpoint can't read from a service later: when the process
/// started and the resolved on-disk paths (both computed in Program.cs). Registered as a singleton.</summary>
public record DiagnosticsInfo(DateTimeOffset StartedAt, string DatabasePath, string SoundsPath);
