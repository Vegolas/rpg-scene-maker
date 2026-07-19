namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's /tv shapes (contracts are duplicated per project by design — keep in sync by hand).

// GET /tv/state — what the shared table screen is showing. Content is null when the screen is cleared;
// Url points at /tv/content/current with the revision baked in as a cache-buster. Both /tv/state and the
// content url are OUTSIDE the API-key gate, so the TV device never needs the admin key.
public record TvContentDto(string Kind, string Url, string? Label);

public record TvStateDto(long Rev, TvContentDto? Content);

// GET /tv/show/recent — recently pushed content, newest first (a protected, panel-only route). File is the
// stored image name: re-push it via /tv/show and preview it via /images/{File} (the panel holds the key).
public record TvRecentItemDto(string Kind, string File, string? Label, DateTimeOffset PushedAtUtc);
