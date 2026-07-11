using RpgSceneMaker.Api.Models;

namespace RpgSceneMaker.Api.Services;

/// <summary>A resolved light plus the scene settings driving its effect.</summary>
public record EffectJob(ILightService Service, string? TargetId, bool IsHue, SceneLight Light);

/// <summary>
/// Runs light effects (flicker/glow/storm/drift) as background loops — one Task per light. Loops are
/// organized into independent <em>groups</em>, each with its own linked cancellation token:
/// <see cref="StartAsync"/> replaces every group (scene-activation semantics — this set is the whole
/// state); <see cref="StartGroupAsync"/> adds a group without disturbing the others (event-timeline
/// clips) and returns a handle for <see cref="StopGroup"/>; <see cref="StopAll"/> cancels every group
/// (reset-lights / a new scene taking over). Each loop is self-healing: it backs off on failure and
/// gives up on a persistently unreachable light, and never crashes the app.
/// </summary>
public class EffectEngine(ILogger<EffectEngine> logger)
{
    private const string WarmWhite = "#FF8C2A";

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Group> _groups = [];

    private sealed record Group(CancellationTokenSource Cts, Task[] Tasks);

    /// <summary>Stop every running group, then run <paramref name="jobs"/> as a fresh group — the scene
    /// activation semantics (this set replaces the whole running state).</summary>
    public Task StartAsync(IReadOnlyList<EffectJob> jobs)
    {
        StopAll();
        StartGroupAsync(jobs);
        return Task.CompletedTask;
    }

    /// <summary>Run <paramref name="jobs"/> as an independent group without disturbing other groups.
    /// Returns a handle for <see cref="StopGroup"/>; <see cref="Guid.Empty"/> when there are no jobs.</summary>
    public Guid StartGroupAsync(IReadOnlyList<EffectJob> jobs)
    {
        if (jobs.Count == 0) return Guid.Empty;

        var handle = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        // Floor Hue command rate across this group's loops so the bridge isn't flooded (~8 cmd/sec).
        var hueLoops = jobs.Count(j => j.IsHue);
        var tasks = jobs.Select(job => Task.Run(() => RunLoopAsync(job, hueLoops, token), token)).ToArray();
        lock (_lock)
            _groups[handle] = new Group(cts, tasks);
        return handle;
    }

    /// <summary>Stop and drain a single group (no-op for <see cref="Guid.Empty"/> or an already-stopped handle).</summary>
    public void StopGroup(Guid handle)
    {
        Group? group;
        lock (_lock)
        {
            if (!_groups.Remove(handle, out group)) return;
        }
        Drain(group);
    }

    public void StopAll()
    {
        Group[] groups;
        lock (_lock)
        {
            groups = [.. _groups.Values];
            _groups.Clear();
        }
        foreach (var group in groups) Drain(group);
    }

    private static void Drain(Group group)
    {
        try { group.Cts.Cancel(); } catch (ObjectDisposedException) { }
        // Don't block the request thread longer than ~2s waiting for loops to drain.
        try { Task.WaitAll(group.Tasks, TimeSpan.FromSeconds(2)); } catch { /* loops observe cancellation and exit */ }
        group.Cts.Dispose();
    }

    private async Task RunLoopAsync(EffectJob job, int hueLoops, CancellationToken token)
    {
        var effect = job.Light.Effect!;
        var rnd = new Random();
        var failures = 0;
        var elapsedMs = 0.0;

        while (!token.IsCancellationRequested)
        {
            int desiredMs;
            try
            {
                desiredMs = effect.Type switch
                {
                    "flicker" => await FlickerAsync(job, effect, rnd, token),
                    "glow" => await GlowAsync(job, effect, elapsedMs, token),
                    "storm" => await StormAsync(job, effect, rnd, token),
                    "drift" => await DriftAsync(job, effect, elapsedMs, token),
                    _ => 1000,
                };
                failures = 0;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (++failures >= 10)
                {
                    logger.LogWarning(ex, "Effect '{Type}' on light '{Key}' failed {Count}x — giving up",
                        effect.Type, job.Light.LightKey, failures);
                    break;
                }
                logger.LogWarning(ex, "Effect '{Type}' on light '{Key}' failed ({Count})",
                    effect.Type, job.Light.LightKey, failures);
                var backoff = failures switch { 1 => 1000, 2 => 5000, _ => 15000 };
                if (!await DelayAsync(backoff, token)) break;
                continue;
            }

            var intervalMs = FloorInterval(desiredMs, job, hueLoops);
            if (!await DelayAsync(intervalMs, token)) break;
            elapsedMs += intervalMs;
        }
    }

    // Hue: keep total command rate under ~8/sec. Tuya is serialized by its semaphore; still floor it.
    internal static int FloorInterval(int desiredMs, EffectJob job, int hueLoops) =>
        job.IsHue
            ? Math.Max(desiredMs, 125 * Math.Max(1, hueLoops))
            : Math.Max(desiredMs, 400);

    private static async Task<bool> DelayAsync(int ms, CancellationToken token)
    {
        try { await Task.Delay(ms, token); return true; }
        catch (OperationCanceledException) { return false; }
    }

    // ---- Effects ----

    // Warm base color randomly dipping/rising in brightness with tiny hue wobble.
    private async Task<int> FlickerAsync(EffectJob job, LightEffect e, Random rnd, CancellationToken token)
    {
        var baseColor = string.IsNullOrWhiteSpace(job.Light.Color) ? WarmWhite : job.Light.Color;
        var baseBri = job.Light.Brightness ?? 80;
        var (r, g, b) = ColorMath.ParseHexColor(baseColor);
        var (h, s, _) = ColorMath.RgbToHsv(r, g, b);

        var jitter = 3 * e.Intensity;                       // ±% swing scaled by intensity
        var bri = Math.Clamp(baseBri + rnd.Next(-jitter, jitter + 1), 1, 100);
        var hue = (h + rnd.Next(-6, 7) + 360) % 360;        // subtle warm wobble
        var hex = ToHex(hue, s, 1.0);

        await job.Service.SetColorAsync(hex, bri, job.TargetId);
        // speed 10 → ~120ms, speed 1 → ~700ms
        return 120 + (10 - e.Speed) * 64;
    }

    // Sine wave around the base brightness; Hue fades match the step for smoothness.
    private async Task<int> GlowAsync(EffectJob job, LightEffect e, double elapsedMs, CancellationToken token)
    {
        var baseBri = job.Light.Brightness ?? 80;
        var amplitude = 4 * e.Intensity;
        var periodMs = 12000 - (e.Speed - 1) * (12000 - 2000) / 9.0;
        var step = job.IsHue ? 1000 : 500;

        var phase = 2 * Math.PI * (elapsedMs % periodMs) / periodMs;
        var bri = (int)Math.Clamp(baseBri + amplitude * Math.Sin(phase), 1, 100);
        int? transition = job.IsHue ? step : null;

        if (!string.IsNullOrWhiteSpace(job.Light.Color))
            await job.Service.SetColorAsync(job.Light.Color, bri, job.TargetId, transition);
        else
            await job.Service.SetWhiteAsync(bri, job.Light.Temperature, job.TargetId, transition);
        return step;
    }

    // Dim cold base with occasional bright flash bursts.
    private async Task<int> StormAsync(EffectJob job, LightEffect e, Random rnd, CancellationToken token)
    {
        var flashColor = e.Colors.Count > 0 ? e.Colors[0] : null;  // null → cold white
        var baseColor = job.Light.Color;                            // null → dim cold blue

        // 1-2 rapid bright pulses.
        var pulses = rnd.Next(1, 3);
        for (var i = 0; i < pulses; i++)
        {
            if (flashColor is not null)
                await job.Service.SetColorAsync(flashColor, 100, job.TargetId);
            else
                await job.Service.SetWhiteAsync(100, 100, job.TargetId); // cold white
            if (!await DelayAsync(rnd.Next(100, 251), token)) return 0;
            await ApplyStormBaseAsync(job, baseColor);
            if (i < pulses - 1 && !await DelayAsync(rnd.Next(100, 201), token)) return 0;
        }

        // Quiet gap until the next strike: faster speed → shorter gaps.
        var maxGap = 6000 - (e.Speed - 1) * (6000 - 1500) / 9;
        return rnd.Next(1200, Math.Max(1300, maxGap));
    }

    private static Task ApplyStormBaseAsync(EffectJob job, string? baseColor) =>
        baseColor is not null
            ? job.Service.SetColorAsync(baseColor, job.Light.Brightness ?? 15, job.TargetId)
            : job.Service.SetColorAsync("#3060FF", job.Light.Brightness ?? 15, job.TargetId); // dim cold blue

    // Interpolate around the color list in HSV space.
    private async Task<int> DriftAsync(EffectJob job, LightEffect e, double elapsedMs, CancellationToken token)
    {
        var n = e.Colors.Count;                             // validated ≥2 for drift
        var cycleMs = 60000 - (e.Speed - 1) * (60000 - 8000) / 9.0;
        var step = 2000;

        var pos = (elapsedMs % cycleMs) / cycleMs * n;      // 0..n
        var i = (int)pos % n;
        var t = pos - Math.Floor(pos);
        var (h0, s0, v0) = ToHsv(e.Colors[i]);
        var (h1, s1, v1) = ToHsv(e.Colors[(i + 1) % n]);

        var h = LerpHue(h0, h1, t);
        var s = s0 + (s1 - s0) * t;
        var v = v0 + (v1 - v0) * t;

        int? transition = job.IsHue ? step : null;
        await job.Service.SetColorAsync(ToHex(h, s, v), job.Light.Brightness, job.TargetId, transition);
        return step;
    }

    // ---- helpers ----

    private static (double h, double s, double v) ToHsv(string hex)
    {
        var (r, g, b) = ColorMath.ParseHexColor(hex);
        return ColorMath.RgbToHsv(r, g, b);
    }

    private static string ToHex(double h, double s, double v)
    {
        var (r, g, b) = ColorMath.HsvToRgb(h, s, v);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    // Interpolate along the shorter arc of the hue circle.
    internal static double LerpHue(double a, double b, double t)
    {
        var delta = ((b - a + 540) % 360) - 180;
        return (a + delta * t + 360) % 360;
    }
}
