using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;

namespace AmbientDirector.Api.Services.Images;

/// <summary>
/// <see cref="IImageSearchSource"/> backed by the Scryfall card-art API (https://scryfall.com/docs/api).
/// Searches Magic: The Gathering cards and maps each to its cropped art (<c>art_crop</c>). The API and its
/// image CDN are public and need no key, but Scryfall requires a descriptive User-Agent and an explicit
/// Accept header, asks callers to cache, and rate-limits (~10 req/s) — so successful searches are
/// memo-cached for 15 minutes.
/// </summary>
public sealed class ScryfallImageSource : IImageSearchSource
{
    // Scryfall serves card art from this host; the import allowlist is exactly this host over https.
    private const string CdnHost = "cards.scryfall.io";
    private const int MaxResults = 60;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;

    public ScryfallImageSource(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
        // Scryfall rejects requests without a descriptive User-Agent and an explicit Accept (see their API
        // docs). Set them once on the injected typed client so every request carries them.
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AmbientDirector", "1.0"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/Vegolas/ambient-director)"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string Id => "scryfall";
    public string Name => "Scryfall";
    public string Attribution => "Card art via Scryfall. Magic: The Gathering © Wizards of the Coast.";

    public async Task<ImageSearchResponseDto> SearchAsync(string query, ImageSearchOptions options, CancellationToken ct)
    {
        // Cache successful searches (Scryfall etiquette + keeps us well under their ~10 req/s limit). The
        // options are part of the key: IncludeExtras changes the query Scryfall runs, and FullImage changes
        // which image URLs each hit maps to — so a bare-query result must never satisfy an options request.
        var cacheKey = $"imgsearch:scryfall:{(options.FullImage ? 'F' : 'a')}{(options.IncludeExtras ? 'X' : '_')}:{query.Trim().ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out ImageSearchResponseDto? cached) && cached is not null)
            return cached;

        // include:extras pulls in tokens, art-series cards, emblems, etc. that Scryfall hides by default.
        var q = options.IncludeExtras ? $"{query} include:extras" : query;
        // unique=art collapses reprints that share the same artwork.
        var url = $"https://api.scryfall.com/cards/search?q={Uri.EscapeDataString(q)}&unique=art";
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new ImageSourceException($"Scryfall is unreachable: {ex.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var result = response.StatusCode switch
            {
                HttpStatusCode.OK => MapSearch(body, options.FullImage),
                // 404 means "no cards matched" — a normal empty result, not a failure.
                HttpStatusCode.NotFound => new ImageSearchResponseDto(Id, 0, false, []),
                // 400 means the search syntax was rejected — surface Scryfall's own explanation.
                HttpStatusCode.BadRequest => throw new ValidationException("error.imageSearch.badQuery", ErrorDetails(body)),
                _ => throw new ImageSourceException($"Scryfall search failed ({(int)response.StatusCode})."),
            };
            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
            return result;
        }
    }

    public bool CanFetch(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && string.Equals(url.Host, CdnHost, StringComparison.OrdinalIgnoreCase);

    public Task<HttpResponseMessage> FetchImageAsync(Uri url, CancellationToken ct) =>
        // ResponseHeadersRead so the endpoint streams the body and enforces the size cap itself.
        _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

    private ImageSearchResponseDto MapSearch(string body, bool fullImage)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var total = root.TryGetProperty("total_cards", out var tc) && tc.TryGetInt32(out var t) ? t : 0;
        var sourceHasMore = root.TryGetProperty("has_more", out var hm) && hm.ValueKind == JsonValueKind.True;

        var results = new List<ImageResultDto>();
        var truncated = false;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var card in data.EnumerateArray())
            {
                foreach (var hit in MapCard(card, fullImage))
                {
                    if (results.Count >= MaxResults) { truncated = true; break; }
                    results.Add(hit);
                }
                if (truncated) break;
            }
        }

        return new ImageSearchResponseDto(Id, total, sourceHasMore || truncated, results);
    }

    // A card yields one result from its own image_uris, or — for double-faced cards with no top-level image —
    // one result per face that has them. A card with no usable image anywhere yields nothing.
    private static IEnumerable<ImageResultDto> MapCard(JsonElement card, bool fullImage)
    {
        if (TryImages(card, fullImage, out var thumb, out var url))
        {
            yield return new ImageResultDto(Str(card, "name") ?? "", Str(card, "artist"), thumb, url);
            yield break;
        }

        if (card.TryGetProperty("card_faces", out var faces) && faces.ValueKind == JsonValueKind.Array)
        {
            var cardName = Str(card, "name");
            var cardArtist = Str(card, "artist");
            foreach (var face in faces.EnumerateArray())
            {
                if (TryImages(face, fullImage, out var faceThumb, out var faceUrl))
                    yield return new ImageResultDto(
                        Str(face, "name") ?? cardName ?? "",
                        Str(face, "artist") ?? cardArtist,
                        faceThumb, faceUrl);
            }
        }
    }

    // Resolve the grid thumbnail + the image to import from a card/face's image_uris. Art mode uses the
    // tight art_crop for both (the historical behaviour). Full-card mode previews a mid-size scan and imports
    // a high-res JPEG for the user to crop in-app; each falls back down the chain so an odd card without a
    // given size still yields something.
    private static bool TryImages(JsonElement el, bool fullImage, out string thumb, out string url)
    {
        thumb = "";
        url = "";
        if (!el.TryGetProperty("image_uris", out var imgs) || imgs.ValueKind != JsonValueKind.Object)
            return false;

        if (fullImage)
        {
            thumb = Pick(imgs, "normal", "large", "small", "png", "art_crop");
            url = Pick(imgs, "large", "png", "normal", "art_crop");
        }
        else
        {
            thumb = url = Pick(imgs, "art_crop");
        }
        return url.Length > 0;
    }

    // First non-empty string URL among the given image_uris keys, in preference order ("" if none).
    private static string Pick(JsonElement imgs, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (imgs.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                && v.GetString() is { Length: > 0 } value)
                return value;
        }
        return "";
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Scryfall error bodies carry a human "details" string; use it as the {0} arg, else a generic fallback.
    private static string ErrorDetails(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.String
                && d.GetString() is { Length: > 0 } details)
                return details;
        }
        catch (JsonException) { /* fall through to the generic arg */ }
        return "the search query could not be understood";
    }
}
