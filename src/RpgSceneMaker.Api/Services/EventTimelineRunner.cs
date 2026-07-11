using System.Collections.Concurrent;
using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Runs an event's advanced <see cref="EventTimeline"/> in the background: sound and light clips placed at
/// millisecond offsets, each scheduled on its own task. Only one timeline runs at a time — starting a new
/// one stops the current one. Light clips overlay the live scene; each animated clip runs as its own group
/// on the shared <see cref="EffectEngine"/> (so a scene activation / reset-lights <c>StopAll</c> stops the
/// clip too — whoever called it is taking over the lights). When the run ends, the event's ending
/// (<see cref="GameEvent.After"/>, via <see cref="EventAfterApplier"/>) runs — by default restoring the prior
/// lighting (live scene, else the default light), or optionally activating another scene / applying the
/// default light. Sound clips overlay current playback like an event's <see cref="GameEvent.SoundEffects"/> do.
///
/// Registered as a singleton, but <see cref="ILightService"/> and the scene/after services are scoped
/// (the provider is chosen per-request from settings), so a run creates one scope for its whole lifetime.
/// </summary>
public sealed class EventTimelineRunner(
    IServiceScopeFactory scopeFactory,
    SoundStore soundStore,
    SoundFileStorage soundFiles,
    SoundboardPlayer player,
    LightFxStore fxStore,
    ILogger<EventTimelineRunner> logger)
{
    private readonly object _lock = new();
    private Run? _current;

    /// <summary>Id of the event whose timeline is running, or null. For the panel's running highlight.</summary>
    public string? RunningEventId
    {
        get { lock (_lock) return _current?.Event.Id; }
    }

    /// <summary>Start <paramref name="evt"/>'s timeline, stopping any current run first (awaiting its
    /// cleanup briefly). Returns immediately; the run continues on a background task.</summary>
    public void Start(GameEvent evt)
    {
        var run = new Run(evt);
        Run? previous;
        lock (_lock)
        {
            previous = _current;
            _current = run;
        }
        if (previous is not null) StopRun(previous);
        run.Task = Task.Run(() => ExecuteAsync(run));
    }

    /// <summary>Stop the running timeline (cancelling it and running its cleanup). Returns whether one was running.</summary>
    public bool Stop()
    {
        Run? current;
        lock (_lock)
        {
            current = _current;
            _current = null;
        }
        if (current is null) return false;
        StopRun(current);
        return true;
    }

    // Cancel a run and wait briefly for its cleanup (light restore etc.) to drain — don't block the
    // request thread indefinitely if a light is unreachable.
    private static void StopRun(Run run)
    {
        try { run.Cts.Cancel(); } catch (ObjectDisposedException) { }
        try { run.Task.Wait(TimeSpan.FromSeconds(2)); } catch { /* run observes cancellation and exits */ }
    }

    private async Task ExecuteAsync(Run run)
    {
        var evt = run.Event;
        var timeline = evt.Timeline!;
        var ct = run.Cts.Token;
        var hasLightClips = timeline.Lights.Count > 0;

        var cancelHandles = new ConcurrentBag<Guid>();    // voices stopped only when the run is cancelled
        var lightGroups = new ConcurrentBag<Guid>();      // effect groups on the global engine, one per animated clip

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var groupLights = sp.GetRequiredService<ILightService>();
        var registry = sp.GetRequiredService<LightRegistry>();
        var globalEffects = sp.GetRequiredService<EffectEngine>();
        var afterApplier = sp.GetRequiredService<EventAfterApplier>();

        // Preload every sound once so a clip doesn't open a DbContext + query mid-playback; a missing id
        // still warns + skips at fire time.
        var sounds = new Dictionary<string, Sound>(StringComparer.OrdinalIgnoreCase);
        foreach (var sound in await soundStore.GetAllAsync()) sounds[sound.Id] = sound;

        // Same for any "fx" light clips: resolve the FX library once up front, not per engine tick.
        Dictionary<string, LightFx>? fxLib = null;
        if (timeline.Lights.Any(c => c.Effect is { Type: "fx" }))
            fxLib = (await fxStore.GetAllAsync()).ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

        try
        {
            // Stop any running scene effects so they don't fight the clips (same reason the flash does).
            if (hasLightClips)
                globalEffects.StopAll();

            var soundTasks = timeline.Sounds
                .Select(clip => RunSoundClipAsync(clip, sounds, cancelHandles, ct));
            var lightTasks = timeline.Lights
                .Select(clip => RunLightClipAsync(clip, groupLights, registry, globalEffects, lightGroups, fxLib, ct));

            await Task.WhenAll(soundTasks.Concat(lightTasks));
        }
        catch (OperationCanceledException) { /* stopped — cleanup below */ }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Event timeline '{Id}' run failed", evt.Id);
        }
        finally
        {
            // Windowless loops and windowed clips parked in cancelHandles are cut only on an actual stop;
            // natural-end one-shots keep playing their tail otherwise.
            if (ct.IsCancellationRequested)
                foreach (var handle in cancelHandles) SafeStopVoice(handle);
            foreach (var handle in lightGroups) SafeStopGroup(globalEffects, handle);

            // Apply what comes after the timeline (restore the prior lighting, activate another scene, or the
            // default light) — but only if a newer run hasn't taken over (its lights win). "previous" only
            // matters when the clips disturbed the lights (the up-front StopAll); an explicit scene/default
            // transition runs regardless, so even a sound-only timeline can end on a scene change.
            if (StillOwnsLights(run) && (hasLightClips || EventAfterApplier.IsTransition(evt.After)))
            {
                try { await afterApplier.ApplyAsync(evt.After); }
                catch (Exception ex) { logger.LogWarning(ex, "Event timeline '{Id}' after-effect failed", evt.Id); }
            }

            lock (_lock)
            {
                if (ReferenceEquals(_current, run)) _current = null;
            }
            run.Cts.Dispose();
        }
    }

    // True when no newer run has replaced this one: _current is null (a plain stop) or still this run.
    private bool StillOwnsLights(Run run)
    {
        lock (_lock)
            return _current is null || ReferenceEquals(_current, run);
    }

    private async Task RunSoundClipAsync(
        TimelineSoundClip clip, IReadOnlyDictionary<string, Sound> sounds,
        ConcurrentBag<Guid> cancelHandles, CancellationToken ct)
    {
        await Task.Delay(clip.StartMs, ct); // OCE here propagates so cancellation surfaces to the run
        Guid handle;
        Sound sound;
        try
        {
            if (!sounds.TryGetValue(clip.SoundId, out var found))
            {
                logger.LogWarning("Event timeline: sound '{Id}' not found — skipped", clip.SoundId);
                return;
            }
            sound = found;
            handle = player.Play(sound.Id, soundFiles.FullPath(sound), sound.Loop, clip.Volume ?? sound.Volume);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Event timeline: sound clip '{Id}' failed", clip.SoundId);
            return;
        }

        if (clip.DurationMs is { } durationMs)
        {
            // Explicit window: cut the voice at its end (harmless if a one-shot already finished).
            cancelHandles.Add(handle);
            try { await Task.Delay(durationMs, ct); }
            finally { SafeStopVoice(handle); }
        }
        else if (sound.Loop)
        {
            // A windowless loop has no natural end — hold the run open indefinitely and let the loop play
            // until the run is stopped (via /events/stop, a re-trigger, or shutdown), then it's cut below.
            cancelHandles.Add(handle);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        else
        {
            // Natural end: hold the run open for the file's length (when known) and let the voice finish
            // itself — it must survive the run's cleanup unless the run is actually stopped. A non-positive
            // DurationMs is the "unmeasurable" sentinel, i.e. unknown, so don't hold on it.
            cancelHandles.Add(handle);
            if (sound.DurationMs is { } naturalMs && naturalMs > 0)
                await Task.Delay(naturalMs, ct);
        }
    }

    private async Task RunLightClipAsync(
        TimelineLightClip clip, ILightService groupLights, LightRegistry registry,
        EffectEngine globalEffects, ConcurrentBag<Guid> lightGroups,
        IReadOnlyDictionary<string, LightFx>? fxLib, CancellationToken ct)
    {
        await Task.Delay(clip.StartMs, ct); // OCE here propagates so cancellation surfaces to the run
        try
        {
            // Null/empty key → the configured provider group (no target); else a registered light.
            ILightService service;
            string? targetId;
            bool isHue;
            if (string.IsNullOrWhiteSpace(clip.LightKey))
            {
                service = groupLights;
                targetId = null;
                isHue = groupLights is HueLightService;
            }
            else
            {
                var resolved = registry.Resolve(clip.LightKey);
                (service, targetId, isHue) = (resolved.Service, resolved.TargetId, resolved.IsHue);
            }

            // A "fx" clip is a live reference to a library FX: resolve it once into a materialized "custom"
            // effect (a missing FX degrades to a static clip). Every other effect type runs as authored.
            var effect = clip.Effect;
            if (effect is { Type: "fx" })
            {
                if (fxLib is not null && effect.FxId is { } fxId && fxLib.TryGetValue(fxId, out var lib))
                    effect = SceneLightApplier.MaterializeFx(lib);
                else
                {
                    logger.LogWarning("Event timeline: Light FX '{FxId}' not found — showing a static clip", effect.FxId);
                    effect = null;
                }
            }

            var sceneLight = new SceneLight
            {
                LightKey = clip.LightKey ?? "",
                Power = clip.Power,
                Color = clip.Color,
                Brightness = clip.Brightness,
                Temperature = clip.Temperature,
                Effect = effect,
            };

            await SceneLightApplier.ApplyBaseAsync(service, targetId, sceneLight);

            if (effect is not null)
            {
                // Run the effect as its own group on the shared engine (not a private one): a global
                // StopAll then correctly stops this clip too when something takes over the lights.
                var handle = globalEffects.StartGroupAsync([new EffectJob(service, targetId, isHue, sceneLight)]);
                lightGroups.Add(handle);
                try { await Task.Delay(clip.DurationMs, ct); }
                finally { globalEffects.StopGroup(handle); }
            }
            else
            {
                // Static clip: hold the state for the window; the post-run restore handles cleanup.
                await Task.Delay(clip.DurationMs, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Event timeline: light clip ({Key}) failed", clip.LightKey ?? "all lights");
        }
    }

    private void SafeStopVoice(Guid handle)
    {
        try { player.StopVoice(handle); }
        catch (Exception ex) { logger.LogWarning(ex, "Event timeline: stopping sound voice failed"); }
    }

    private void SafeStopGroup(EffectEngine engine, Guid handle)
    {
        try { engine.StopGroup(handle); }
        catch (Exception ex) { logger.LogWarning(ex, "Event timeline: stopping light effect failed"); }
    }

    private sealed class Run(GameEvent evt)
    {
        public GameEvent Event { get; } = evt;
        public CancellationTokenSource Cts { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;
    }
}
