using System.Globalization;

namespace AmbientDirector.Api.Errors;

/// <summary>
/// The one template renderer shared by server-side localization (<see cref="Services.LocaleService.Localize"/>)
/// and the English fallback baked into exceptions (<see cref="ErrorMessages"/>). Resolves a dotted code to a
/// template via the given lookup, flattens any <see cref="CtxRef"/> argument one level (a localizable
/// sentence fragment), then fills the template's <c>{0}</c>/<c>{1}</c>… placeholders with the invariant
/// culture. Never throws — a missing template or a placeholder/arg mismatch degrades to the code itself,
/// because this feeds <see cref="System.Exception.Message"/>, which must never throw.
/// </summary>
public static class ErrorRender
{
    public static string Format(Func<string, string?> lookup, string code, IReadOnlyList<object?>? args)
    {
        var template = lookup(code);
        if (template is null) return code;
        if (args is null || args.Count == 0) return template;
        try
        {
            var rendered = new object?[args.Count];
            for (var i = 0; i < args.Count; i++)
                rendered[i] = args[i] is CtxRef ctx ? RenderCtx(lookup, ctx) : args[i];
            return string.Format(CultureInfo.InvariantCulture, template, rendered);
        }
        catch (FormatException)
        {
            return code;
        }
    }

    // Localize a context fragment (one level only — the templates are flat, see CtxRef).
    private static string RenderCtx(Func<string, string?> lookup, CtxRef ctx)
    {
        var template = lookup(ctx.Code) ?? ctx.Code;
        if (ctx.Args is null || ctx.Args.Length == 0) return template;
        try { return string.Format(CultureInfo.InvariantCulture, template, ctx.Args); }
        catch (FormatException) { return ctx.Code; }
    }
}
