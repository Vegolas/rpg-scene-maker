namespace RpgSceneMaker.Ui.Contracts;

// UI copies of the /sounds/library + /setup/freesound wire shapes (duplicated by hand per the project's
// duplicated-DTO convention — see the API's Contracts/SoundContracts.cs + FreesoundConfigInput.cs).

// One search hit from the online library. PreviewUrl is the public HQ-MP3 the browser can play directly;
// License is a short label ("CC BY 4.0") and LicenseUrl its deed link; DurationMs is the clip length.
public record SoundSearchResultDto(int Id, string Name, string Author, string License, string? LicenseUrl,
    int DurationMs, string PreviewUrl, IReadOnlyList<string> Tags);

// A page of online-library search results. HasMore is set when another page is available.
public record SoundSearchDto(IReadOnlyList<SoundSearchResultDto> Results, int Page, bool HasMore, int TotalCount);

// Mutable class — the Settings form binds the token input straight to it (like AssistantConfigDto). The API
// never echoes the token, so ApiKey stays "" on load; the user types it to (re)configure.
public class FreesoundConfigDto
{
    public string ApiKey { get; set; } = "";
    public bool Configured { get; set; }
}
