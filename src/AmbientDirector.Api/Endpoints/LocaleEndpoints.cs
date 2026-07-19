using AmbientDirector.Api.Services;

namespace AmbientDirector.Api.Endpoints;

public static class LocaleEndpoints
{
    public static void MapLocaleEndpoints(this WebApplication app)
    {
        // UI translations for the panel. Read-only queries (not Stream-Deck commands), so plain GET —
        // no GetOrPost. The panel reads the whole list and the chosen language's strings client-side.
        var i18n = app.MapGroup("/i18n");

        // Literal segment, so it wins over "/{code}" — same precedence trick as /screens/list.
        i18n.MapGet("/list", (LocaleService locales) => locales.List());

        i18n.MapGet("/{code}", (string code, LocaleService locales) =>
            locales.Get(code) is { } doc ? Results.Ok(doc) : Results.NotFound());
    }
}
