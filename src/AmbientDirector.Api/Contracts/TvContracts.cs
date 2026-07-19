namespace AmbientDirector.Api.Contracts;

// Wire DTOs for the /tv endpoints. The internal TvContent carries the stored image file name; the panel and
// the TV never see it — they get a ready-to-fetch url instead (mirror these by hand in the UI's Contracts/
// per the project's duplicated-DTO convention).

// GET /tv/state response. Content is null when the screen is cleared. Url points at /tv/content/current with
// the current revision as a cache-buster, so a swapped image is fetched fresh without a Cache-Control dance.
public record TvContentDto(string Kind, string Url, string? Label);

public record TvStateDto(long Rev, TvContentDto? Content);

// One entry of GET /tv/show/recent (a protected, panel-only convenience). Exposes the stored File so the
// panel can re-push it (POST /tv/show?image=File) and preview it via /images/{File} (the panel holds the key).
public record TvRecentItemDto(string Kind, string File, string? Label, DateTimeOffset PushedAtUtc);
