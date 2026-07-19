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

    public async Task<ImageSearchResponseDto> SearchAsync(string query, CancellationToken ct)
    {
        // Cache successful searches (Scryfall etiquette + keeps us well under their ~10 req/s limit).
        var cacheKey = $"imgsearch:scryfall:{query.Trim().ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out ImageSearchResponseDto? cached) && cached is not null)
            return cached;

        // unique=art collapses reprints that share the same artwork.
        var url = $"https://api.scryfall.com/cards/search?q={Uri.EscapeDataString(query)}&unique=art";
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
                HttpStatusCode.OK => MapSearch(body),
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

    private ImageSearchResponseDto MapSearch(string body)
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
                foreach (var hit in MapCard(card))
                {
                    if (results.Count >= MaxResults) { truncated = true; break; }
                    results.Add(hit);
                }
                if (truncated) break;
            }
        }

        return new ImageSearchResponseDto(Id, total, sourceHasMore || truncated, results);
    }

    // A card yields one result from its own art_crop, or — for double-faced cards with no top-level image —
    // one result per face that has its own art_crop. A card with no art_crop anywhere yields nothing.
    private static IEnumerable<ImageResultDto> MapCard(JsonElement card)
    {
        if (TryArtCrop(card, out var art))
        {
            yield return new ImageResultDto(Str(card, "name") ?? "", Str(card, "artist"), art, art);
            yield break;
        }

        if (card.TryGetProperty("card_faces", out var faces) && faces.ValueKind == JsonValueKind.Array)
        {
            var cardName = Str(card, "name");
            var cardArtist = Str(card, "artist");
            foreach (var face in faces.EnumerateArray())
            {
                if (TryArtCrop(face, out var faceArt))
                    yield return new ImageResultDto(
                        Str(face, "name") ?? cardName ?? "",
                        Str(face, "artist") ?? cardArtist,
                        faceArt, faceArt);
            }
        }
    }

    private static bool TryArtCrop(JsonElement el, out string artCrop)
    {
        artCrop = "";
        if (el.TryGetProperty("image_uris", out var imgs) && imgs.ValueKind == JsonValueKind.Object
            && imgs.TryGetProperty("art_crop", out var ac) && ac.ValueKind == JsonValueKind.String
            && ac.GetString() is { Length: > 0 } value)
        {
            artCrop = value;
            return true;
        }
        return false;
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
