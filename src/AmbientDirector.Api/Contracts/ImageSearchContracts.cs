namespace AmbientDirector.Api.Contracts;

/// <summary>A selectable image source, surfaced by GET /images/sources. <c>Attribution</c> is an English
/// legal-ish credit line shown under the picker (source-specific integration text stays English per repo
/// convention).</summary>
public record ImageSourceDto(string Id, string Name, string Attribution);

/// <summary>One search hit. <c>ThumbUrl</c> is what the picker grid renders; <c>Url</c> is what
/// POST /images/import fetches (for Scryfall both are the card's art_crop). <c>Detail</c> is a secondary
/// line — the artist for Scryfall — and is nullable.</summary>
public record ImageResultDto(string Title, string? Detail, string ThumbUrl, string Url);

/// <summary>A page of image-search results, returned by GET /images/search. <c>Total</c> is the source's
/// reported match count; <c>HasMore</c> is true when more results exist beyond this page (the source says
/// so, or we truncated to the per-source cap).</summary>
public record ImageSearchResponseDto(string Source, int Total, bool HasMore, IReadOnlyList<ImageResultDto> Results);

/// <summary>Request body for POST /images/import: the picked image URL to fetch and store server-side.</summary>
public record ImageImportInput(string? Url);
