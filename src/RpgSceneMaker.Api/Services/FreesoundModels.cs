namespace RpgSceneMaker.Api.Services;

/// <summary>One sound parsed from Freesound (a search-result row or a single-sound fetch). <see cref="Author"/>
/// is Freesound's uploader "username"; <see cref="LicenseUrl"/> is the raw Creative Commons deed link;
/// <see cref="PreviewUrl"/> is the public "preview-hq-mp3" link (null if the sound has no HQ preview).</summary>
public record FreesoundSound(
    int Id,
    string Name,
    string Author,
    string LicenseUrl,
    int DurationMs,
    string? PreviewUrl,
    IReadOnlyList<string> Tags);

/// <summary>A page of Freesound search results plus paging info (<see cref="HasMore"/> is set when the API
/// reported a "next" page).</summary>
public record FreesoundSearchResult(
    IReadOnlyList<FreesoundSound> Results,
    int Page,
    bool HasMore,
    int TotalCount);
