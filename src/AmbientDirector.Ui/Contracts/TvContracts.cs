namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's /tv shapes (contracts are duplicated per project by design — keep in sync by hand).

// GET /tv/state content. For kind "image", Url points at /tv/content/current (with the current rev baked in as
// a cache-buster) and Board is null. For kind "board", Url is null and Board carries the full render model
// (image refs already resolved to the open /tv/content/board/{name} routes). Both /tv/state and the content
// urls are OUTSIDE the API-key gate, so the TV device never needs the admin key.
public record TvContentDto(string Kind, string? Url, string? Label, TvBoardDto? Board = null);

public record TvStateDto(long Rev, TvContentDto? Content);

// Render model for kind=board — everything BoardCanvas needs to draw the layout. Image refs are pre-resolved to
// ready-to-fetch urls: the TV's open /tv/content/board/{name} routes from the API, or (in the panel) key-bearing
// /images urls built via BoardRender.ToRenderModel. Elements are in paint order (index 0 = bottom).
public record TvBoardDto(string? BackgroundColor, string? BackgroundUrl, List<TvBoardElementDto> Elements);

// One element of a board's render model. For kind "image", Url is the image url and the text fields are null;
// for kind "text", Url is null and Text/Color/Size/Align carry the content; for kind "party" AND kind "enemies"
// (both live-roster elements), Party carries the live roster and the other content fields are null — "party"
// reads Players/Counters, "enemies" reads Enemies. Geometry is percent-of-stage.
public record TvBoardElementDto(string Kind, double X, double Y, double W, double H,
    string? Url, string? Text, string? Color, double? Size, string? Align, TvPartyDto? Party = null);

// Live render model for a kind=party / kind=enemies board element: the player roster + table-level counters +
// the encounter's enemy roster (issue #120) at render time. Portrait refs are pre-resolved to ready-to-fetch
// urls — the TV's open /tv/content/board/{name} route (with ?rev= as a cache-buster) from the API, or (in the
// panel) key-bearing /images urls built via PartyRender.ToRenderModel.
public record TvPartyDto(List<TvPartyPlayerDto> Players, List<TvPartyCounterDto> Counters, List<TvEnemyDto> Enemies);

public record TvPartyPlayerDto(string Name, string? PortraitUrl, List<TvPartyCounterDto> Counters);

// One enemy in the render model: portrait + text + counter tracks + the spotlight (boss) flag (issue #122).
// PortraitUrl is a ready-to-fetch url (null when the enemy has no portrait). SpotlightLabel (issue #128) is the
// active game system's literal chip text (Daggerheart's "SPOTLIGHT"), resolved server-side / panel-side from
// the GameSystemDto; null (no system) hides the chip. Mirrors the API's TvEnemyDto — keep the two in sync.
public record TvEnemyDto(string Name, string? PortraitUrl, bool Spotlight, List<TvPartyCounterDto> Counters,
    string? SpotlightLabel = null);

// One counter track. Glyph + Color (issue #128) are the resolved presentation: the counter's semantic Key
// mapped to the active game system's preset (member/enemy/table scope) → its curated Glyph name (null = plain
// dot) and content Color (hex — null = the glyph's self-styled default / neutral dot). No key / no match / no
// system → both null. BoardCanvas draws straight from these, so it stays system-agnostic.
public record TvPartyCounterDto(string Label, int Value, int? Max, string? Style,
    string? Glyph = null, string? Color = null);

// GET /tv/show/recent — recently pushed content, newest first (a protected, panel-only route). Ref is the
// stored image file name for kind "image", or the board id for kind "board"; re-push it via /tv/show (an image
// through /images/{Ref}, a board by id). The panel holds the key so both preview fine.
public record TvRecentItemDto(string Kind, string Ref, string? Label, DateTimeOffset PushedAtUtc);
