namespace AmbientDirector.Api.Contracts;

/// <summary>Wire shape for a sound-effect library entry. <c>Image</c> is an optional full-art tile
/// background; <c>DurationMs</c> is the file's natural length (null when it can't be decoded); <c>Waveform</c>
/// is a compact amplitude preview (peaks 0–255; null/empty when not yet measured or undecodable). Both feed
/// the event timeline editor. <c>Waveform</c> serializes as base64. <c>Author</c>/<c>License</c>/
/// <c>LicenseUrl</c>/<c>SourceUrl</c> are attribution for a library-imported sound (all null for a plain
/// upload); they are server-set and not editable via <see cref="SoundUpdateInput"/>.</summary>
public record SoundDto(string Id, string Name, string Category, double Volume, bool Loop, string? Image, int? DurationMs, byte[]? Waveform,
    string? Author = null, string? License = null, string? LicenseUrl = null, string? SourceUrl = null);

/// <summary>Editable fields for a sound; each null field is left unchanged (partial update), except
/// <see cref="Image"/> which is set as sent (null clears the tile background). Attribution fields are
/// deliberately absent — they are set only by a library import, never edited here.</summary>
public record SoundUpdateInput(string? Name, string? Category, double? Volume, bool? Loop, string? Image);

/// <summary>Ids of the sounds currently playing on the server, for the panel's live highlight.</summary>
public record SoundStateDto(IReadOnlyList<string> Playing);

/// <summary>One search hit from the online sound library (Freesound). <c>PreviewUrl</c> is the public HQ-MP3
/// preview the browser can play directly; <c>License</c> is a short label ("CC BY 4.0") and <c>LicenseUrl</c>
/// its deed link; <c>DurationMs</c> is the clip length.</summary>
public record SoundSearchResultDto(int Id, string Name, string Author, string License, string? LicenseUrl,
    int DurationMs, string PreviewUrl, IReadOnlyList<string> Tags);

/// <summary>A page of online-library search results. <c>HasMore</c> is set when another page is available;
/// <c>TotalCount</c> is the library's reported total match count.</summary>
public record SoundSearchDto(IReadOnlyList<SoundSearchResultDto> Results, int Page, bool HasMore, int TotalCount);

/// <summary>Body of <c>POST /sounds/library/import</c>: the library sound <see cref="Id"/> to import, with an
/// optional name/category override (defaults to the library title and no category).</summary>
public record SoundLibraryImportInput(int Id, string? Name, string? Category);
