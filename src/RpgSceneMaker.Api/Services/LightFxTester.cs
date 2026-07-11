using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Runs a bounded preview of a library <see cref="LightFx"/> on a chosen light (or the configured provider
/// group) so it can be auditioned from the editor. Only one test runs at a time — starting a new one stops
/// the current one. The FX is materialized into a "custom" effect and started as its own group on the shared
/// <see cref="EffectEngine"/> for the window, then the group is stopped and the lighting restored (the live
/// scene, else the configured default) via <see cref="SceneLightApplier.RestoreLightsAsync"/>.
///
/// The initial base state is applied synchronously in <see cref="StartAsync"/> so an unconfigured/unreachable
/// light surfaces to the caller as a proper error (mapped by the request middleware) rather than failing
/// silently in the background loop. Registered as a singleton, but <see cref="ILightService"/> and
/// <see cref="SceneLightApplier"/> are scoped, so a run owns one scope for its whole lifetime.
/// </summary>
public sealed class LightFxTester(IServiceScopeFactory scopeFactory, ILogger<LightFxTester> logger)
{
    private readonly object _lock = new();
    private Run? _current;

    /// <summary>Start a bounded test of <paramref name="fx"/> on <paramref name="lightKey"/> (null/empty = the
    /// configured provider group) for <paramref name="seconds"/>, stopping any current test first. The base
    /// state is applied synchronously (may throw for an unknown key / unreachable light); the timed window
    /// then runs on a background task.</summary>
    public async Task StartAsync(LightFx fx, string? lightKey, int seconds)
    {
        Stop(); // cancel + drain any previous test before taking over the lights

        var scope = scopeFactory.CreateScope();
        try
        {
            var sp = scope.ServiceProvider;
            var registry = sp.GetRequiredService<LightRegistry>();
            var effects = sp.GetRequiredService<EffectEngine>();

            ILightService service;
            string? targetId;
            bool isHue;
            if (string.IsNullOrWhiteSpace(lightKey))
            {
                // The configured provider group (no target), like the legacy scene "all lights" block.
                service = sp.GetRequiredService<ILightService>();
                targetId = null;
                isHue = service is HueLightService;
            }
            else
            {
                var resolved = registry.Resolve(lightKey); // throws for an unknown key → 400
                (service, targetId, isHue) = (resolved.Service, resolved.TargetId, resolved.IsHue);
            }

            var sceneLight = new SceneLight
            {
                LightKey = lightKey ?? "",
                Power = true,
                Effect = new LightEffect { Type = "custom", Keyframes = fx.Keyframes, Loop = fx.Loop, CycleMs = fx.CycleMs },
            };

            // Reach a sensible base (power on) before the first keyframe; awaited so an unconfigured/unreachable
            // light throws here and the middleware maps it, instead of silently backing off in the loop.
            await SceneLightApplier.ApplyBaseAsync(service, targetId, sceneLight);

            var handle = effects.StartGroupAsync([new EffectJob(service, targetId, isHue, sceneLight)]);
            var run = new Run(fx.Id, scope, effects, handle);
            lock (_lock) _current = run;
            run.Task = Task.Run(() => RunWindowAsync(run, seconds));
            scope = null; // ownership handed to the run's background task
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <summary>Stop the running test (if any), draining its cleanup. Returns whether one was running.</summary>
    public bool Stop()
    {
        Run? current;
        lock (_lock)
        {
            current = _current;
            _current = null;
        }
        if (current is null) return false;
        try { current.Cts.Cancel(); } catch (ObjectDisposedException) { }
        try { current.Task.Wait(TimeSpan.FromSeconds(2)); } catch { /* run observes cancellation and exits */ }
        return true;
    }

    private async Task RunWindowAsync(Run run, int seconds)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(seconds), run.Cts.Token); }
        catch (OperationCanceledException) { /* stopped early — cleanup below */ }
        finally
        {
            run.Effects.StopGroup(run.Handle);
            // Restore whatever should be showing (the live scene, else the default light), like an event's flash.
            try
            {
                var applier = run.Scope.ServiceProvider.GetRequiredService<SceneLightApplier>();
                await applier.RestoreLightsAsync();
            }
            catch (Exception ex) { logger.LogWarning(ex, "Light FX test '{Id}' restore failed", run.FxId); }

            lock (_lock)
            {
                if (ReferenceEquals(_current, run)) _current = null;
            }
            run.Cts.Dispose();
            run.Scope.Dispose();
        }
    }

    private sealed class Run(string fxId, IServiceScope scope, EffectEngine effects, Guid handle)
    {
        public string FxId { get; } = fxId;
        public IServiceScope Scope { get; } = scope;
        public EffectEngine Effects { get; } = effects;
        public Guid Handle { get; } = handle;
        public CancellationTokenSource Cts { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;
    }
}
