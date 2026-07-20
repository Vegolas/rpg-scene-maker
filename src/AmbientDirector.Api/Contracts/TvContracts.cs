namespace AmbientDirector.Api.Contracts;

// Wire DTOs for the /tv endpoints. The internal TvContent carries the stored image file name / board id; the
// panel and the TV never see raw file names on the content path — they get ready-to-fetch urls instead (image
// refs are pre-resolved to the gate-validated /tv/content/* routes). Mirror these by hand in the UI's
// Contracts/ per the project's duplicated-DTO convention.

// GET /tv/state content. For kind "image", Url points at /tv/content/current (with the current revision as a
// cache-buster) and Board is null. For kind "board", Url is null and Board carries the full render model.
public record TvContentDto(string Kind, string? Url, string? Label, TvBoardDto? Board = null);

public record TvStateDto(long Rev, TvContentDto? Content);

// Render model for kind=board — everything the key-free TV needs to draw the layout. Image refs are already
// resolved to the gate-validated /tv/content/board/{name} route (with ?rev= as a cache-buster, like
// /tv/content/current), so the TV never touches the general /images route. BackgroundColor is the stored
// "#RRGGBB" (or null → renderer default). Elements are in paint order (index 0 = bottom).
public record TvBoardDto(string? BackgroundColor, string? BackgroundUrl, List<TvBoardElementDto> Elements);

// One element of a board's render model. For kind "image", Url is the /tv/content/board/{name} route and the
// text fields are null; for kind "text", Url is null and Text/Color/Size/Align carry the content; for kind
// "party" AND kind "enemies" (both live-roster elements), Party carries the live roster and the other content
// fields are null — the enemies element reads the roster's Enemies, the party element its Players/Counters.
// Geometry is percent-of-stage (see Models/Board.cs).
public record TvBoardElementDto(string Kind, double X, double Y, double W, double H,
    string? Url, string? Text, string? Color, double? Size, string? Align, TvPartyDto? Party = null);

// Live render model for a kind=party / kind=enemies board element: the player roster + table-level counters +
// the enemy roster at poll time. One instance is built per /tv/state and shared by both element kinds (a
// synthesized encounter view populates all three — heroes as Players, Fear as Counters, instances as Enemies).
// Portrait refs are pre-resolved to the gate-validated /tv/content/board/{name} route like every other board image.
public record TvPartyDto(List<TvPartyPlayerDto> Players, List<TvPartyCounterDto> Counters, List<TvEnemyDto> Enemies);

public record TvPartyPlayerDto(string Name, string? PortraitUrl, List<TvPartyCounterDto> Counters);

// One enemy in the render model: portrait + text + counter tracks + the spotlight (boss) flag (issue #122 gave
// enemies portraits, like players). PortraitUrl is pre-resolved to the gate-validated /tv/content/board/{name}
// route (null when the enemy has no portrait).
public record TvEnemyDto(string Name, string? PortraitUrl, bool Spotlight, List<TvPartyCounterDto> Counters);

public record TvPartyCounterDto(string Label, int Value, int? Max, string? Style);

// One entry of GET /tv/show/recent (a protected, panel-only convenience). Exposes the raw Ref (a stored image
// file name for kind "image", a board id for kind "board") so the panel can re-push it (POST /tv/show).
public record TvRecentItemDto(string Kind, string Ref, string? Label, DateTimeOffset PushedAtUtc);
