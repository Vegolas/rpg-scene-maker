using RpgSceneMaker.Api.Services;

namespace RpgSceneMaker.Api.Errors;

/// <summary>
/// The English rendering of an error code, used for <see cref="System.Exception.Message"/> (server logs and
/// the ultimate fallback when a locale file lacks the key). Templates come from the embedded
/// <c>en.json</c> — the single source of truth — so throw sites never duplicate the English text. The table
/// is loaded once and cached; rendering never throws (see <see cref="ErrorRender"/>).
/// </summary>
public static class ErrorMessages
{
    private static readonly IReadOnlyDictionary<string, string> En = LocaleService.ShippedStrings("en");

    public static string English(string code, IReadOnlyList<object?>? args) =>
        ErrorRender.Format(key => En.TryGetValue(key, out var v) ? v : null, code, args);
}
