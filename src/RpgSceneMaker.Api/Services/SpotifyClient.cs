using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RpgSceneMaker.Api.Errors;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Shared, process-wide cache of the current Spotify access token. Registered as a singleton so that
/// the transient-per-resolution <see cref="SpotifyClient"/> (via AddHttpClient) doesn't re-refresh on
/// every request. The semaphore serialises token refreshes across concurrent scene activation.
/// </summary>
public class SpotifyTokenCache
{
    public string? AccessToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public readonly SemaphoreSlim Gate = new(1, 1);
}

/// <summary>
/// Client for the Spotify Web API (Authorization Code + PKCE, no client secret). Spotify has no local
/// API, so this controls a Spotify Connect device on the LAN via the cloud Web API. Premium is required
/// for playback control.
/// </summary>
public class SpotifyClient(HttpClient http, SpotifyStore store, SpotifyTokenCache cache)
{
    private const string TokenEndpoint = "https://accounts.spotify.com/api/token";
    private const string ApiBase = "https://api.spotify.com";

    private static readonly HashSet<string> UriTypes = new(StringComparer.Ordinal)
    {
        "track", "playlist", "album", "artist",
    };

    // ---------- tokens ----------

    public async Task<string> GetAccessTokenAsync()
    {
        var config = store.Current;
        if (!config.IsConnected)
            throw new NotConfiguredException("error.notConfigured.spotifyConnect");

        if (IsFresh()) return cache.AccessToken!;

        await cache.Gate.WaitAsync();
        try
        {
            if (IsFresh()) return cache.AccessToken!;

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = config.RefreshToken,
                ["client_id"] = config.ClientId,
            });
            using var response = await http.PostAsync(TokenEndpoint, content);
            await CacheTokenResponseAsync(response, requireRefreshToken: false);
            return cache.AccessToken!;
        }
        finally
        {
            cache.Gate.Release();
        }
    }

    /// <summary>PKCE code-for-token exchange. Persists the refresh token and caches the access token.</summary>
    public async Task ExchangeCodeAsync(string code, string redirectUri, string codeVerifier)
    {
        var config = store.Current;
        if (!config.IsConfigured)
            throw new NotConfiguredException("error.notConfigured.spotifyClientIdSet");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = config.ClientId,
            ["code_verifier"] = codeVerifier,
        });
        using var response = await http.PostAsync(TokenEndpoint, content);
        await CacheTokenResponseAsync(response, requireRefreshToken: true);
    }

    private bool IsFresh() =>
        cache.AccessToken is not null && cache.ExpiresAt - DateTimeOffset.UtcNow > TimeSpan.FromSeconds(60);

    private async Task CacheTokenResponseAsync(HttpResponseMessage response, bool requireRefreshToken)
    {
        var payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new SpotifyException($"Spotify token request failed ({(int)response.StatusCode}): {payload}");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
            throw new SpotifyException("Spotify token response had no access_token.");

        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetInt32() : 3600;

        cache.AccessToken = accessToken;
        cache.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        if (root.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { Length: > 0 } refresh)
            store.SaveTokens(refresh);
        else if (requireRefreshToken)
            throw new SpotifyException("Spotify token response had no refresh_token.");
    }

    // ---------- playback ----------

    public async Task PlayAsync(string uri)
    {
        if (!TryParseUri(uri, out var type, out var id))
            throw new ValidationException("error.music.badUri", uri);
        var canonical = $"spotify:{type}:{id}";

        object body = type == "track"
            ? new { uris = new[] { canonical } }
            : new { context_uri = canonical };

        var deviceId = store.Current.PreferredDeviceId;
        if (!string.IsNullOrEmpty(deviceId))
        {
            using var first = await SendAsync(HttpMethod.Put,
                $"/v1/me/player/play?device_id={Uri.EscapeDataString(deviceId)}", body);
            if (first.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(first);
                return;
            }
            // The preferred device is gone — fall through and let Spotify pick the active device.
        }

        using var response = await SendAsync(HttpMethod.Put, "/v1/me/player/play", body);
        await EnsureSuccessAsync(response);
    }

    /// <summary>Pause playback. When best-effort (throwOnNoDevice=false) a missing device is a no-op.</summary>
    public async Task PauseAsync(bool throwOnNoDevice = true)
    {
        var deviceId = store.Current.PreferredDeviceId;
        var path = string.IsNullOrEmpty(deviceId)
            ? "/v1/me/player/pause"
            : $"/v1/me/player/pause?device_id={Uri.EscapeDataString(deviceId)}";

        using var response = await SendAsync(HttpMethod.Put, path);
        if (response.StatusCode == HttpStatusCode.NotFound && !throwOnNoDevice)
            return;
        await EnsureSuccessAsync(response);
    }

    public async Task SetVolumeAsync(double volume01)
    {
        var percent = (int)Math.Round(Math.Clamp(volume01, 0, 1) * 100);
        var path = $"/v1/me/player/volume?volume_percent={percent}";
        var deviceId = store.Current.PreferredDeviceId;
        if (!string.IsNullOrEmpty(deviceId))
            path += $"&device_id={Uri.EscapeDataString(deviceId)}";

        using var response = await SendAsync(HttpMethod.Put, path);
        await EnsureSuccessAsync(response);
    }

    /// <summary>Resume the current playback context (no body — Spotify keeps the current queue/track).</summary>
    public async Task ResumeAsync()
    {
        var deviceId = store.Current.PreferredDeviceId;
        if (!string.IsNullOrEmpty(deviceId))
        {
            using var first = await SendAsync(HttpMethod.Put,
                $"/v1/me/player/play?device_id={Uri.EscapeDataString(deviceId)}");
            if (first.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(first);
                return;
            }
            // The preferred device is gone — fall through and let Spotify pick the active device.
        }

        using var response = await SendAsync(HttpMethod.Put, "/v1/me/player/play");
        await EnsureSuccessAsync(response);
    }

    public Task NextAsync() => PlayerCommandAsync(HttpMethod.Post, "/v1/me/player/next");
    public Task PreviousAsync() => PlayerCommandAsync(HttpMethod.Post, "/v1/me/player/previous");

    public Task SetShuffleAsync(bool shuffle) =>
        PlayerCommandAsync(HttpMethod.Put, $"/v1/me/player/shuffle?state={(shuffle ? "true" : "false")}");

    /// <summary>Set the repeat mode. Public vocabulary is off|track|playlist; Spotify's API uses "context" for playlist.</summary>
    public Task SetRepeatAsync(string mode)
    {
        var state = mode == "playlist" ? "context" : mode; // off | track | context
        return PlayerCommandAsync(HttpMethod.Put, $"/v1/me/player/repeat?state={state}");
    }

    // Player command that targets the preferred device when one is set; keeps the friendly no-device 404 message.
    private async Task PlayerCommandAsync(HttpMethod method, string path)
    {
        var deviceId = store.Current.PreferredDeviceId;
        if (!string.IsNullOrEmpty(deviceId))
            path += (path.Contains('?') ? "&" : "?") + $"device_id={Uri.EscapeDataString(deviceId)}";

        using var response = await SendAsync(method, path);
        await EnsureSuccessAsync(response);
    }

    // ---------- library / search / state ----------

    public async Task<List<SpotifyDevice>> GetDevicesAsync()
    {
        var root = await GetJsonAsync("/v1/me/player/devices");
        var list = new List<SpotifyDevice>();
        if (root.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
            foreach (var d in devices.EnumerateArray())
                list.Add(new SpotifyDevice(
                    Str(d, "id"),
                    Str(d, "name"),
                    Str(d, "type"),
                    d.TryGetProperty("is_active", out var a) && a.ValueKind == JsonValueKind.True));
        return list;
    }

    public async Task<List<SpotifyPlaylist>> GetPlaylistsAsync()
    {
        var list = new List<SpotifyPlaylist>();
        string? path = "/v1/me/playlists?limit=50";
        while (path is not null && list.Count < 200)
        {
            var root = await GetJsonAsync(path);
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var p in items.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new SpotifyPlaylist(Str(p, "id"), Str(p, "name"), Str(p, "uri"),
                        FirstImageUrl(p), PlaylistTrackTotal(p)));
                }
            path = NextPath(root);
        }
        return list;
    }

    public async Task<List<SpotifyTrack>> SearchTracksAsync(string query)
    {
        // Development-mode Spotify apps reject search limits above 10 with a bare "Invalid limit" 400.
        var root = await GetJsonAsync($"/v1/search?type=track&limit=10&q={Uri.EscapeDataString(query)}");
        var list = new List<SpotifyTrack>();
        if (root.TryGetProperty("tracks", out var tracks) && tracks.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array)
            foreach (var t in items.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) continue;
                string? image = t.TryGetProperty("album", out var album) ? FirstImageUrl(album) : null;
                list.Add(new SpotifyTrack(Str(t, "id"), Str(t, "name"), JoinArtists(t), Str(t, "uri"), image));
            }
        return list;
    }

    public async Task<SpotifyPlaybackState?> GetStateAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/v1/me/player");
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureSuccessAsync(response);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        var isPlaying = root.TryGetProperty("is_playing", out var p) && p.ValueKind == JsonValueKind.True;

        string? trackName = null, artistName = null;
        double? durationSeconds = null;
        if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            trackName = Str(item, "name") is { Length: > 0 } n ? n : null;
            artistName = JoinArtists(item) is { Length: > 0 } a ? a : null;
            if (item.TryGetProperty("duration_ms", out var dm) && dm.ValueKind == JsonValueKind.Number)
                durationSeconds = dm.GetInt64() / 1000.0;
        }

        double? progressSeconds = null;
        if (root.TryGetProperty("progress_ms", out var pm) && pm.ValueKind == JsonValueKind.Number)
            progressSeconds = pm.GetInt64() / 1000.0;

        string? contextUri = null;
        if (root.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.Object)
            contextUri = ctx.TryGetProperty("uri", out var cu) ? cu.GetString() : null;

        var isShuffling = root.TryGetProperty("shuffle_state", out var ss) && ss.ValueKind == JsonValueKind.True;

        // Spotify reports "context" for playlist/album repeat — map it back to our public "playlist".
        var repeat = root.TryGetProperty("repeat_state", out var rs) && rs.ValueKind == JsonValueKind.String
            ? rs.GetString() switch { "context" => "playlist", "track" => "track", _ => "off" }
            : "off";

        string? deviceName = null;
        int? volume = null;
        if (root.TryGetProperty("device", out var dev) && dev.ValueKind == JsonValueKind.Object)
        {
            deviceName = dev.TryGetProperty("name", out var dn) ? dn.GetString() : null;
            if (dev.TryGetProperty("volume_percent", out var vp) && vp.ValueKind == JsonValueKind.Number)
                volume = vp.GetInt32();
        }

        return new SpotifyPlaybackState(isPlaying, trackName, artistName, contextUri, deviceName, volume,
            progressSeconds, durationSeconds, isShuffling, repeat);
    }

    // ---------- URI helpers ----------

    /// <summary>Normalise a track/playlist/album/artist reference to canonical <c>spotify:{type}:{id}</c>.</summary>
    public static string NormalizeUri(string id)
    {
        if (TryParseUri(id, out var type, out var spotifyId))
            return $"spotify:{type}:{spotifyId}";
        throw new ValidationException("error.music.badUri", id);
    }

    /// <summary>True for both <c>spotify:{type}:{id}</c> and <c>https://open.spotify.com/{type}/{id}</c>.</summary>
    public static bool IsSpotifyUri(string id) => TryParseUri(id, out _, out _);

    private static bool TryParseUri(string input, out string type, out string id)
    {
        type = "";
        id = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();

        if (input.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Split(':');
            if (parts.Length >= 3 && UriTypes.Contains(parts[1].ToLowerInvariant())
                && !string.IsNullOrEmpty(parts[2]))
            {
                type = parts[1].ToLowerInvariant();
                id = parts[2];
                return true;
            }
            return false;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && uri.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase))
        {
            // AbsolutePath drops the query string; tolerate a leading /intl-xx/ locale segment.
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var offset = segments.Length > 0
                         && segments[0].StartsWith("intl-", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (segments.Length >= offset + 2 && UriTypes.Contains(segments[offset].ToLowerInvariant())
                && !string.IsNullOrEmpty(segments[offset + 1]))
            {
                type = segments[offset].ToLowerInvariant();
                id = segments[offset + 1];
                return true;
            }
        }
        return false;
    }

    // ---------- transport ----------

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        await EnsureSuccessAsync(response);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        var token = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(method, ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await http.SendAsync(request);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync();
        throw response.StatusCode switch
        {
            HttpStatusCode.NotFound => new SpotifyException(
                "No active Spotify device — open Spotify on a device (or pick one in Settings) and try again."),
            HttpStatusCode.Forbidden when detail.Contains("PREMIUM_REQUIRED", StringComparison.OrdinalIgnoreCase) =>
                new SpotifyException("Spotify Premium is required to control playback."),
            _ => new SpotifyException($"Spotify returned {(int)response.StatusCode}: {detail}"),
        };
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string JoinArtists(JsonElement el)
    {
        if (!el.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
            return "";
        return string.Join(", ", artists.EnumerateArray()
            .Select(a => Str(a, "name"))
            .Where(n => !string.IsNullOrEmpty(n)));
    }

    private static string? FirstImageUrl(JsonElement el)
    {
        if (el.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
            foreach (var img in images.EnumerateArray())
                if (img.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    return u.GetString();
        return null;
    }

    // Spotify deprecated the per-playlist "tracks" ref object in favour of "items"; on many accounts the
    // legacy "tracks.total" now reports 0 while the new "items.total" carries the real count. Read both
    // and take whichever is populated so the count is right regardless of which field Spotify fills.
    private static int PlaylistTrackTotal(JsonElement playlist) =>
        Math.Max(TotalOf(playlist, "items"), TotalOf(playlist, "tracks"));

    private static int TotalOf(JsonElement playlist, string prop) =>
        playlist.TryGetProperty(prop, out var o) && o.ValueKind == JsonValueKind.Object
        && o.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number
            ? t.GetInt32() : 0;

    private static string? NextPath(JsonElement root)
    {
        if (root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
            && next.GetString() is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.PathAndQuery;
        return null;
    }
}

public class SpotifyException(string message) : Exception(message);
