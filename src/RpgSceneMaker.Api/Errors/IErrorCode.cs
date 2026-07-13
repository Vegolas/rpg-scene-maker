namespace RpgSceneMaker.Api.Errors;

/// <summary>
/// An exception that carries a stable, machine-readable error code plus the arguments to interpolate into
/// its localized message. The error middleware (see Program.cs) reads these to localize the ProblemDetails
/// <c>detail</c> against the panel's language and to emit a <c>code</c>/<c>args</c> extension for machine
/// consumers (Stream Deck, MCP, tests). <see cref="Code"/> is a dotted key into the locale files, e.g.
/// <c>error.common.nameRequired</c>.
/// </summary>
public interface IErrorCode
{
    string Code { get; }
    IReadOnlyList<object?> Args { get; }
}

/// <summary>
/// A localizable sentence fragment naming which light / effect a validation message is about — e.g.
/// "light 'lamp'", "timeline all lights", "Light FX 'torch'". Passed as an argument into a parent error
/// template and localized one level deep by <see cref="ErrorRender"/>, so the whole sentence translates.
/// <see cref="Code"/> is a dotted <c>error.ctx.*</c> key. The one nested case ("timeline light 'x'") has its
/// own composite key, so the renderer never needs to recurse past a single level.
/// </summary>
public readonly record struct CtxRef(string Code, object?[] Args)
{
    public static CtxRef Light(string key) => new("error.ctx.light", [key]);
    public static CtxRef AllLights() => new("error.ctx.allLights", []);
    public static CtxRef LightFx(string id) => new("error.ctx.lightFx", [id]);
    public static CtxRef TimelineLight(string key) => new("error.ctx.timelineLight", [key]);
    public static CtxRef TimelineAllLights() => new("error.ctx.timelineAllLights", []);
}
