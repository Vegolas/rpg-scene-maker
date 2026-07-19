using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Models;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Validation;
using Xunit;

namespace AmbientDirector.Tests.Unit;

/// <summary>The "custom" keyframe effect: EffectEngine.CustomPosition's active-keyframe/next-boundary math
/// and the keyframe rules in LightValidation.ValidateEffect.</summary>
public class CustomKeyframeTests
{
    // ---- CustomPosition: non-loop ----

    [Fact]
    public void NonLoop_walks_keyframes_then_holds_the_last_forever()
    {
        int[] ats = [0, 100];
        Assert.Equal((0, 100.0), EffectEngine.CustomPosition(ats, loop: false, cycleMs: 0, elapsedMs: 0));
        Assert.Equal((0, 100.0), EffectEngine.CustomPosition(ats, loop: false, cycleMs: 0, elapsedMs: 50));

        var (index, next) = EffectEngine.CustomPosition(ats, loop: false, cycleMs: 0, elapsedMs: 100);
        Assert.Equal(1, index);
        Assert.True(double.IsPositiveInfinity(next)); // last keyframe holds

        (index, next) = EffectEngine.CustomPosition(ats, loop: false, cycleMs: 0, elapsedMs: 99_999);
        Assert.Equal(1, index);
        Assert.True(double.IsPositiveInfinity(next));
    }

    [Fact]
    public void NonLoop_waits_before_a_delayed_first_keyframe()
    {
        int[] ats = [500];
        Assert.Equal((-1, 500.0), EffectEngine.CustomPosition(ats, loop: false, cycleMs: 0, elapsedMs: 0));
        Assert.Equal((-1, 500.0), EffectEngine.CustomPosition(ats, loop: false, cycleMs: 0, elapsedMs: 499));

        var (index, next) = EffectEngine.CustomPosition(ats, loop: false, cycleMs: 0, elapsedMs: 500);
        Assert.Equal(0, index);
        Assert.True(double.IsPositiveInfinity(next));
    }

    // ---- CustomPosition: loop ----

    [Fact]
    public void Loop_wraps_with_a_fresh_global_index_so_the_first_keyframe_refires()
    {
        // The thunder strobe: white @0, off @100, 400ms cycle.
        int[] ats = [0, 100];

        Assert.Equal((0, 100.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 400, elapsedMs: 0));
        Assert.Equal((1, 400.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 400, elapsedMs: 100));
        Assert.Equal((1, 400.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 400, elapsedMs: 399));

        // Cycle 1: the same keyframes get new global indexes (2, 3), so the caller re-sends them.
        Assert.Equal((2, 500.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 400, elapsedMs: 400));
        Assert.Equal((3, 800.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 400, elapsedMs: 500));
        Assert.Equal((3, 800.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 400, elapsedMs: 799));
    }

    [Fact]
    public void Loop_holds_the_previous_cycles_last_keyframe_before_a_delayed_first()
    {
        int[] ats = [100, 200];

        // Cycle 0 before the first keyframe: nothing has fired yet.
        Assert.Equal((-1, 100.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 500, elapsedMs: 0));
        Assert.Equal((0, 200.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 500, elapsedMs: 100));
        Assert.Equal((1, 600.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 500, elapsedMs: 200));

        // Cycle 1 at 520ms sits before this cycle's first keyframe (600): the previous cycle's last keyframe
        // (global index 1) still holds, so nothing is re-sent until 600.
        Assert.Equal((1, 600.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 500, elapsedMs: 520));
        Assert.Equal((2, 700.0), EffectEngine.CustomPosition(ats, loop: true, cycleMs: 500, elapsedMs: 600));
    }

    [Fact]
    public void Loop_global_index_is_monotonic_and_visits_every_firing()
    {
        // Polling between keyframes re-reports the active index (the caller skips the re-send); what must
        // hold is that indexes never go backwards and every firing of 10 cycles × 2 keyframes shows up.
        int[] ats = [0, 100];
        var seen = new HashSet<long>();
        var last = long.MinValue;
        for (var t = 0.0; t < 4000; t += 100)
        {
            var (index, _) = EffectEngine.CustomPosition(ats, loop: true, cycleMs: 400, elapsedMs: t);
            Assert.True(index >= last, $"index went backwards at t={t}");
            last = index;
            seen.Add(index);
        }
        Assert.Equal([.. Enumerable.Range(0, 20).Select(i => (long)i)], [.. seen.Order()]);
    }

    // ---- validation ----

    private static LightEffect Custom(bool loop = false, int? cycleMs = null, params LightKeyframe[] kfs) =>
        new() { Type = "custom", Keyframes = [.. kfs], Loop = loop, CycleMs = cycleMs };

    private static LightKeyframe White(int atMs) => new() { AtMs = atMs, Color = "#ffffff", Brightness = 100 };
    private static LightKeyframe Off(int atMs) => new() { AtMs = atMs, Power = false };

    [Fact]
    public void Strobe_validates_and_normalizes_colors()
    {
        var fx = Custom(loop: true, cycleMs: 400, White(0), Off(100));
        LightValidation.ValidateEffect(fx, CtxRef.Light("test"));
        Assert.Equal("#FFFFFF", fx.Keyframes[0].Color);
    }

    [Fact]
    public void NonLoop_drops_a_stale_cycle_length()
    {
        var fx = Custom(loop: false, cycleMs: 1234, White(0), Off(100));
        LightValidation.ValidateEffect(fx, CtxRef.Light("test"));
        Assert.Null(fx.CycleMs);
    }

    [Fact]
    public void Rejects_empty_descending_and_too_close_keyframes()
    {
        Assert.ThrowsAny<ArgumentException>(() => LightValidation.ValidateEffect(Custom(), CtxRef.Light("test")));
        Assert.ThrowsAny<ArgumentException>(() => LightValidation.ValidateEffect(Custom(kfs: [White(200), Off(100)]), CtxRef.Light("test")));
        Assert.ThrowsAny<ArgumentException>(() => LightValidation.ValidateEffect(Custom(kfs: [White(0), Off(50)]), CtxRef.Light("test")));
    }

    [Fact]
    public void Rejects_a_keyframe_that_sets_nothing()
    {
        var fx = Custom(kfs: [new LightKeyframe { AtMs = 0 }]);
        Assert.ThrowsAny<ArgumentException>(() => LightValidation.ValidateEffect(fx, CtxRef.Light("test")));
    }

    [Fact]
    public void Rejects_a_keyframe_past_the_ten_minute_cap()
    {
        var fx = Custom(kfs: [White(600_001)]);
        Assert.ThrowsAny<ArgumentException>(() => LightValidation.ValidateEffect(fx, CtxRef.Light("test")));
    }

    [Fact]
    public void Loop_needs_a_cycle_at_least_100ms_past_the_last_keyframe()
    {
        Assert.ThrowsAny<ArgumentException>(() => LightValidation.ValidateEffect(
            Custom(loop: true, cycleMs: null, White(0), Off(100)), CtxRef.Light("test")));
        Assert.ThrowsAny<ArgumentException>(() => LightValidation.ValidateEffect(
            Custom(loop: true, cycleMs: 199, White(0), Off(100)), CtxRef.Light("test")));
        LightValidation.ValidateEffect(Custom(loop: true, cycleMs: 200, White(0), Off(100)), CtxRef.Light("test"));
    }
}
