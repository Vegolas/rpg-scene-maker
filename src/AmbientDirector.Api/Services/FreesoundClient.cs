using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using AmbientDirector.Api.Errors;

namespace AmbientDirector.Api.Services;

/// <summary>
/// Client for the Freesound.org API (token-only auth — <c>Authorization: Token {key}</c>). Searches the
/// CC-licensed sound library and fetches single-sound metadata; the HQ MP3 preview is downloaded server-side
/// at import so the token never leaves the server. The base URL comes from config key <c>Freesound:BaseUrl</c>
/// (default <c>https://freesound.org</c>) so verification can point it at a local mock.
/// </summary>
public class FreesoundClient
{
    private const int PageSize = 24;
    private const string Fields = "id,name,username,license,duration,previews,tags";

    private readonly HttpClient _http;
    private readonly FreesoundStore _store;
    private readonly string _base;

    public FreesoundClient(HttpClient http, FreesoundStore store, IConfiguration config)
    {
        _http = http;
        _store = store;
        _base = (config["Freesound:BaseUrl"] ?? "https://freesound.org").TrimEnd('/');
    }

    /// <summary>Full-text search. Page is 1-based; the endpoint rejects a blank query before calling this.</summary>
    public async Task<FreesoundSearchResult> SearchAsync(string query, int page, CancellationToken ct = default)
    {
        var url = $"{_base}/apiv2/search/text/?query={Uri.EscapeDataString(query)}&page={page}" +
                  $"&page_size={PageSize}&fields={Fields}";
        var root = await GetJsonAsync(url, ct);

        var results = new List<FreesoundSound>();
        if (root.TryGetProperty("results", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var s in items.EnumerateArray())
                if (s.ValueKind == JsonValueKind.Object)
                    results.Add(Parse(s));

        var total = root.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetInt32() : results.Count;
        // Freesound sends the next-page URL in "next" (null on the last page).
        var hasMore = root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                      && !string.IsNullOrEmpty(next.GetString());
        return new FreesoundSearchResult(results, page, hasMore, total);
    }

    /// <summary>Fetch one sound's metadata (name, author, license, HQ preview URL) by its Freesound id.</summary>
    public async Task<FreesoundSound> GetSoundAsync(int id, CancellationToken ct = default)
    {
        var root = await GetJsonAsync($"{_base}/apiv2/sounds/{id}/?fields={Fields}", ct);
        return Parse(root);
    }

    /// <summary>Download a preview (the HQ MP3) as bytes. The token is attached in case Freesound wants it;
    /// the public CDN ignores it.</summary>
    public async Task<byte[]> DownloadPreviewAsync(string previewUrl, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, previewUrl);
        Authorize(request);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // ---------- transport ----------

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(request);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    // Attach the token header; a missing key is a 503 rather than a doomed unauthenticated call.
    private void Authorize(HttpRequestMessage request)
    {
        var key = _store.Current.ApiKey;
        if (string.IsNullOrEmpty(key))
            throw new NotConfiguredException("error.notConfigured.freesound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        // Read (and discard) the body so the connection can be reused; the raw text isn't surfaced.
        await response.Content.ReadAsStringAsync();
        throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new FreesoundException("error.freesound.unauthorized")
            : new FreesoundException("error.freesound.requestFailed", (int)response.StatusCode);
    }

    private static FreesoundSound Parse(JsonElement s)
    {
        var id = s.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : 0;
        var durationMs = s.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(d.GetDouble() * 1000) : 0;

        string? preview = null;
        if (s.TryGetProperty("previews", out var previews) && previews.ValueKind == JsonValueKind.Object
            && previews.TryGetProperty("preview-hq-mp3", out var hq) && hq.ValueKind == JsonValueKind.String)
            preview = hq.GetString();

        var tags = new List<string>();
        if (s.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
            foreach (var tag in t.EnumerateArray())
                if (tag.ValueKind == JsonValueKind.String && tag.GetString() is { Length: > 0 } tv)
                    tags.Add(tv);

        return new FreesoundSound(id, Str(s, "name"), Str(s, "username"), Str(s, "license"), durationMs, preview, tags);
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

/// <summary>
/// A Freesound API failure — classified to HTTP 502 by <see cref="ErrorClassifier"/>. Unlike the plain
/// <see cref="SpotifyException"/>, it carries a localizable <see cref="IErrorCode"/> (an <c>error.freesound.*</c>
/// code + args) so the panel and machine consumers can branch on it.
/// </summary>
public sealed class FreesoundException(string code, params object?[] args) : Exception, IErrorCode
{
    public string Code => code;
    public IReadOnlyList<object?> Args => args;
    public override string Message => ErrorMessages.English(code, args);
}
