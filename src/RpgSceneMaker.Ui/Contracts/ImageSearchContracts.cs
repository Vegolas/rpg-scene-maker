namespace RpgSceneMaker.Ui.Contracts;

// Mirrors the API's image-search contracts (contracts are duplicated per project by design — keep in sync
// by hand). Backs the in-app art picker (GET images/sources, GET images/search, POST images/import).

// A selectable image source (GET images/sources). Attribution is an English credit line the picker shows
// under the grid (deliberately not localized — it's source-specific legal text supplied by the server).
public record ImageSourceDto(string Id, string Name, string Attribution);

// One search hit. ThumbUrl is what the grid renders; Url is what images/import fetches (for Scryfall both
// are the card's art_crop). Detail is a secondary line — the artist for Scryfall — and is nullable.
public record ImageResultDto(string Title, string? Detail, string ThumbUrl, string Url);

// A page of search results (GET images/search). Total is the source's reported match count; HasMore is true
// when more results exist beyond this page.
public record ImageSearchResponseDto(string Source, int Total, bool HasMore, IReadOnlyList<ImageResultDto> Results);
