namespace AmbientDirector.Ui.Contracts;

// UI copies of the /i18n wire DTOs (contracts are duplicated per project by convention — keep in sync
// with AmbientDirector.Api/Contracts/LocaleContracts.cs).

// A language offered in the picker. Code = file name (BCP-47, "en"/"pl"); Name = endonym ("Polski").
public record LocaleInfo(string Code, string Name, string EnglishName);

// One language's flat map of dotted keys ("nav.scenes") to translated text.
public record LocaleDocument(string Code, string Name, string EnglishName, Dictionary<string, string> Strings);
