using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using RpgSceneMaker.Ui.Contracts;

namespace RpgSceneMaker.Ui.Services;

/// <summary>All communication with the Scene Maker API, with the optional API key attached.</summary>
public class ApiClient(HttpClient http, IJSRuntime js, UiState ui)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? _apiKey;
    private bool _keyLoaded;

    // ---------- api key (persisted in the browser) ----------

    public async Task<string?> GetApiKeyAsync()
    {
        if (!_keyLoaded)
        {
            _apiKey = await js.InvokeAsync<string?>("localStorage.getItem", "apiKey");
            _keyLoaded = true;
        }
        return _apiKey;
    }

    public async Task SetApiKeyAsync(string? key)
    {
        _apiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        _keyLoaded = true;
        if (_apiKey is null)
            await js.InvokeVoidAsync("localStorage.removeItem", "apiKey");
        else
            await js.InvokeVoidAsync("localStorage.setItem", "apiKey", _apiKey);
    }

    // ---------- scenes ----------

    public async Task<List<SceneDto>> GetScenesAsync() =>
        await GetAsync<List<SceneDto>>("scenes") ?? [];

    public Task<ActiveSceneDto?> GetActiveSceneAsync() => GetAsync<ActiveSceneDto?>("scenes/active");

    /// <summary>Registered lights the scene editor and Lights tab target individually; empty (silent) when offline.</summary>
    public async Task<List<RegisteredLightInfo>> GetRegisteredLightsAsync() =>
        await GetAsync<List<RegisteredLightInfo>>("lights/list") ?? [];

    public Task<SceneDto?> GetSceneAsync(string id) => GetAsync<SceneDto?>($"scenes/{Uri.EscapeDataString(id)}");

    /// <summary>Upsert a scene; the editor shows the returned error inline / via toast.</summary>
    public Task<(SceneDto? Result, string? Error)> SaveSceneAsync(string id, SceneEdit edit) =>
        FetchAsync<SceneDto>(HttpMethod.Put, $"scenes/{Uri.EscapeDataString(id)}", edit.ToDto());

    public async Task<(bool Ok, string? Error)> DeleteSceneAsync(string id)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Delete, $"scenes/{Uri.EscapeDataString(id)}");
            ui.SetConnected(true);
            return response.IsSuccessStatusCode ? (true, null) : (false, await ExtractProblemAsync(response));
        }
        catch (Exception ex)
        {
            ui.SetConnected(false);
            return (false, $"API unreachable: {ex.Message}");
        }
    }

    public async Task<ActivationDto?> ActivateSceneAsync(string id)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, $"scenes/{id}/activate");
            var result = await response.Content.ReadFromJsonAsync<ActivationDto>(Json);
            ui.SetConnected(true);
            return result;
        }
        catch (Exception ex)
        {
            ReportTransportError(ex);
            return null;
        }
    }

    // ---------- fire-and-forget commands (lights, music, sfx) ----------

    public async Task<bool> CommandAsync(string path, string? okMessage = null)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, path);
            ui.SetConnected(true);
            if (!response.IsSuccessStatusCode)
            {
                ui.ReportError(await ExtractProblemAsync(response));
                return false;
            }
            if (okMessage is not null) ui.ReportOk(okMessage);
            return true;
        }
        catch (Exception ex)
        {
            ReportTransportError(ex);
            return false;
        }
    }

    // ---------- setup / configuration ----------

    public Task<(LightingConfigDto? Result, string? Error)> GetConfigAsync() =>
        FetchAsync<LightingConfigDto>(HttpMethod.Get, "setup/config");

    public async Task<bool> SaveConfigAsync(LightingConfigDto config)
    {
        var (_, error) = await FetchAsync<LightingConfigDto>(HttpMethod.Put, "setup/config", config);
        if (error is not null)
        {
            ui.ReportError(error);
            return false;
        }
        return true;
    }

    public Task<(List<BridgeDto>? Result, string? Error)> DiscoverBridgesAsync() =>
        FetchAsync<List<BridgeDto>>(HttpMethod.Get, "setup/hue/discover");

    public Task<(HueRegistrationDto? Result, string? Error)> RegisterHueAsync(string bridgeIp) =>
        FetchAsync<HueRegistrationDto>(HttpMethod.Post, $"setup/hue/register?bridgeIp={Uri.EscapeDataString(bridgeIp)}");

    public Task<(List<HueLightDto>? Result, string? Error)> GetHueLightsAsync(string bridgeIp, string appKey) =>
        FetchAsync<List<HueLightDto>>(HttpMethod.Get,
            $"setup/hue/lights?bridgeIp={Uri.EscapeDataString(bridgeIp)}&appKey={Uri.EscapeDataString(appKey)}");

    public Task<(List<DiscoveredTuyaDto>? Result, string? Error)> ScanTuyaAsync() =>
        FetchAsync<List<DiscoveredTuyaDto>>(HttpMethod.Get, "setup/scan?seconds=8");

    /// <summary>Request with an explicit error channel — the settings wizard shows failures inline.</summary>
    private async Task<(T? Result, string? Error)> FetchAsync<T>(HttpMethod method, string path, object? body = null)
    {
        try
        {
            var request = new HttpRequestMessage(method, path);
            if (await GetApiKeyAsync() is { } key)
                request.Headers.Add("X-Api-Key", key);
            if (body is not null)
                request.Content = JsonContent.Create(body, options: Json);

            using var response = await http.SendAsync(request);
            ui.SetConnected(true);
            if (!response.IsSuccessStatusCode)
                return (default, await ExtractProblemAsync(response));
            return (await response.Content.ReadFromJsonAsync<T>(Json), null);
        }
        catch (TaskCanceledException)
        {
            return (default, "The request timed out — the device did not answer.");
        }
        catch (Exception ex)
        {
            ui.SetConnected(false);
            return (default, $"API unreachable: {ex.Message}");
        }
    }

    // ---------- spotify (music) ----------

    public Task<(SpotifyConfigDto? Result, string? Error)> GetSpotifyConfigAsync() =>
        FetchAsync<SpotifyConfigDto>(HttpMethod.Get, "setup/spotify/config");

    public async Task<bool> SaveSpotifyConfigAsync(SpotifyConfigDto config)
    {
        var (_, error) = await FetchAsync<JsonNode>(HttpMethod.Put, "setup/spotify/config", config);
        if (error is not null)
        {
            ui.ReportError(error);
            return false;
        }
        return true;
    }

    public Task<(List<SpotifyDeviceDto>? Result, string? Error)> GetSpotifyDevicesAsync() =>
        FetchAsync<List<SpotifyDeviceDto>>(HttpMethod.Get, "setup/spotify/devices");

    public Task<bool> DisconnectSpotifyAsync() =>
        CommandAsync("setup/spotify/disconnect", "Spotify disconnected");

    public Task<(List<SpotifyPlaylistDto>? Result, string? Error)> GetSpotifyPlaylistsAsync() =>
        FetchAsync<List<SpotifyPlaylistDto>>(HttpMethod.Get, "music/playlists");

    public Task<(List<SpotifyTrackDto>? Result, string? Error)> SearchSpotifyTracksAsync(string query) =>
        FetchAsync<List<SpotifyTrackDto>>(HttpMethod.Get, $"music/search?q={Uri.EscapeDataString(query)}");

    public Task<SpotifyStateDto?> GetSpotifyStateAsync() => GetAsync<SpotifyStateDto?>("music/state");

    /// <summary>URL for a full-page redirect to start the Spotify login (with the API key when set).</summary>
    public async Task<string> GetSpotifyLoginUrlAsync()
    {
        const string path = "setup/spotify/login";
        return await GetApiKeyAsync() is { } key
            ? $"{path}?apiKey={Uri.EscapeDataString(key)}"
            : path;
    }

    // ---------- logs ----------

    /// <summary>Recent server log entries (newest first) for the Logs tab; silent on failure like other pollers.</summary>
    public async Task<List<LogEntryDto>> GetLogsAsync() =>
        await GetAsync<List<LogEntryDto>>("logs/list") ?? [];

    public Task<bool> ClearLogsAsync() => CommandAsync("logs/clear", "Logs cleared");

    // ---------- plumbing ----------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (await GetApiKeyAsync() is { } key)
            request.Headers.Add("X-Api-Key", key);
        return await http.SendAsync(request);
    }

    /// <summary>GET with silent failure — pollers use this so a hiccup only flips the connection dot.</summary>
    private async Task<T?> GetAsync<T>(string path)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, path);
            ui.SetConnected(true);
            if (!response.IsSuccessStatusCode) return default;
            return await response.Content.ReadFromJsonAsync<T>(Json);
        }
        catch (Exception)
        {
            ui.SetConnected(false);
            return default;
        }
    }

    private static async Task<string> ExtractProblemAsync(HttpResponseMessage response)
    {
        try
        {
            var node = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var title = node?["title"]?.GetValue<string>();
            var detail = node?["detail"]?.GetValue<string>();
            return (title, detail) switch
            {
                (not null, not null) => $"{title}: {detail}",
                (not null, null) => title,
                _ => $"HTTP {(int)response.StatusCode}",
            };
        }
        catch
        {
            return $"HTTP {(int)response.StatusCode}";
        }
    }

    private void ReportTransportError(Exception ex)
    {
        ui.SetConnected(false);
        ui.ReportError($"API unreachable: {ex.Message}");
    }
}
