namespace AmbientDirector.Api.Contracts;

// A language the panel can switch to, surfaced by GET /i18n/list. Code is the file name (BCP-47,
// e.g. "en", "pl"); Name is the endonym shown in the picker ("Polski"); EnglishName is for sorting/labels.
public record LocaleInfo(string Code, string Name, string EnglishName);

// One language's full string table, served by GET /i18n/{code}. Strings is a flat map of dotted keys
// ("nav.scenes") to translated text; the panel falls back per-key to English for anything missing here.
public record LocaleDocument(string Code, string Name, string EnglishName, Dictionary<string, string> Strings);
