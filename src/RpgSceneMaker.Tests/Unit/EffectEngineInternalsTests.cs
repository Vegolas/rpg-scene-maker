using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;
using Xunit;

namespace RpgSceneMaker.Tests.Unit;

public class EffectEngineInternalsTests
{
    private static EffectJob Job(bool isHue) =>
        new(null!, null, isHue, new SceneLight { LightKey = "x" });

    [Fact]
    public void FloorInterval_tuya_floors_at_400ms()
    {
        Assert.Equal(400, EffectEngine.FloorInterval(100, Job(isHue: false), hueLoops: 0));
        Assert.Equal(700, EffectEngine.FloorInterval(700, Job(isHue: false), hueLoops: 0)); // above the floor untouched
    }

    [Fact]
    public void FloorInterval_hue_floor_scales_with_loop_count()
    {
        // 125ms per concurrent Hue loop, so the whole bridge stays under ~8 cmd/sec.
        Assert.Equal(125, EffectEngine.FloorInterval(50, Job(isHue: true), hueLoops: 1));
        Assert.Equal(250, EffectEngine.FloorInterval(50, Job(isHue: true), hueLoops: 2));
        Assert.Equal(1000, EffectEngine.FloorInterval(1000, Job(isHue: true), hueLoops: 2)); // above the floor untouched
    }

    [Fact]
    public void LerpHue_takes_the_shorter_arc_across_zero()
    {
        // 350 -> 10 is +20 through 0, not -340 the long way.
        Assert.Equal(10.0, EffectEngine.LerpHue(350, 10, 1.0), 6);
        Assert.Equal(0.0, EffectEngine.LerpHue(350, 10, 0.5), 6);   // midpoint sits on 0
    }

    [Fact]
    public void LerpHue_goes_backwards_across_zero_the_other_way()
    {
        // 10 -> 350 is -20 through 0.
        Assert.Equal(350.0, EffectEngine.LerpHue(10, 350, 1.0), 6);
        Assert.Equal(0.0, EffectEngine.LerpHue(10, 350, 0.5), 6);
    }

    [Fact]
    public void LerpHue_endpoints_are_exact()
    {
        Assert.Equal(120.0, EffectEngine.LerpHue(120, 240, 0.0), 6);
        Assert.Equal(180.0, EffectEngine.LerpHue(120, 240, 0.5), 6);
        Assert.Equal(240.0, EffectEngine.LerpHue(120, 240, 1.0), 6);
    }
}
