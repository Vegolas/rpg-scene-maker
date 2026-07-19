using AmbientDirector.Api.Services;
using Xunit;

namespace AmbientDirector.Tests.Unit;

public class ActivationResultTests
{
    [Theory]
    [InlineData("ok", "ok", "ok")]
    [InlineData("skipped", "skipped", "skipped")]
    [InlineData("ok", "skipped", "ok")]
    [InlineData("skipped", "ok", "skipped")]
    public void FullySucceeded_when_every_part_is_ok_or_skipped(string light, string music, string sfx) =>
        Assert.True(new ActivationResult("s", light, music, sfx).FullySucceeded);

    [Theory]
    [InlineData("error: boom", "ok", "ok")]
    [InlineData("ok", "error: no device", "ok")]
    [InlineData("ok", "skipped", "error: missing file")]
    public void Not_FullySucceeded_when_any_part_errored(string light, string music, string sfx) =>
        Assert.False(new ActivationResult("s", light, music, sfx).FullySucceeded);
}
