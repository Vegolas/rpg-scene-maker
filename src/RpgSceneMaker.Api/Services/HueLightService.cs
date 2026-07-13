using System.Text;
using System.Text.Json;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Errors;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Controls Philips Hue lights through the Hue Bridge local REST API.
/// Targets the configured light ids, or every light on the bridge (group 0) when the list is empty.
/// </summary>
public class HueLightService(HttpClient http, SettingsStore settings) : ILightService
{
    private HueConfig Opts => settings.Current.Hue;

    public Task SetPowerAsync(bool on, string? targetId = null, int? transitionMs = null) =>
        SetStateAsync(new Dictionary<string, object> { ["on"] = on }, targetId, transitionMs);

    public async Task<bool> ToggleAsync()
    {
        var newState = !await IsOnAsync();
        await SetPowerAsync(newState);
        return newState;
    }

    public Task SetColorAsync(string hexColor, int? brightnessPercent = null, string? targetId = null, int? transitionMs = null)
    {
        var (r, g, b) = ColorMath.ParseHexColor(hexColor);
        var (h, s, v) = ColorMath.RgbToHsv(r, g, b);
        if (brightnessPercent is int bp)
            v = Math.Clamp(bp / 100.0, 0.01, 1.0);

        return SetStateAsync(new Dictionary<string, object>
        {
            ["on"] = true,
            ["hue"] = (int)Math.Round(h / 360 * 65535),
            ["sat"] = (int)Math.Round(s * 254),
            ["bri"] = ToBri(v * 100),
        }, targetId, transitionMs);
    }

    public Task SetWhiteAsync(int brightnessPercent, int? temperaturePercent = null, string? targetId = null, int? transitionMs = null)
    {
        var state = new Dictionary<string, object>
        {
            ["on"] = true,
            ["bri"] = ToBri(brightnessPercent),
            // Neutral-ish saturation reset so a previous colour scene doesn't tint the white.
            ["sat"] = 0,
        };
        if (temperaturePercent is int tp)
        {
            // Hue ct is in mireds: 500 = warmest, 153 = coldest. Our scale: 0 warm - 100 cold.
            state["ct"] = 500 - (int)Math.Round(Math.Clamp(tp, 0, 100) * (500 - 153) / 100.0);
            state.Remove("sat"); // ct implies white mode by itself
        }
        return SetStateAsync(state, targetId, transitionMs);
    }

    public Task SetBrightnessAsync(int percent, string? targetId = null, int? transitionMs = null) =>
        SetStateAsync(new Dictionary<string, object> { ["on"] = true, ["bri"] = ToBri(percent) }, targetId, transitionMs);

    public async Task<object> GetStatusAsync()
    {
        EnsureConfigured();
        return await GetAsync("/lights");
    }

    private static int ToBri(double percent) =>
        Math.Clamp((int)Math.Round(percent * 254 / 100), 1, 254);

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Opts.BridgeIp) || string.IsNullOrWhiteSpace(Opts.AppKey))
            throw new NotConfiguredException("error.notConfigured.hue");
    }

    private string BaseUrl => $"http://{Opts.BridgeIp}/api/{Opts.AppKey}";

    private async Task<bool> IsOnAsync()
    {
        EnsureConfigured();
        if (Opts.LightIds.Count > 0)
        {
            var light = await GetAsync($"/lights/{Opts.LightIds[0]}");
            return light.TryGetProperty("state", out var s) && s.TryGetProperty("on", out var on) && on.GetBoolean();
        }
        var group = await GetAsync("/groups/0");
        return group.TryGetProperty("state", out var gs) && gs.TryGetProperty("any_on", out var anyOn) && anyOn.GetBoolean();
    }

    private async Task SetStateAsync(Dictionary<string, object> state, string? targetId = null, int? transitionMs = null)
    {
        EnsureConfigured();
        // Hue transitiontime is in units of 100ms.
        if (transitionMs is int ms)
            state["transitiontime"] = Math.Max(0, (int)Math.Round(ms / 100.0));

        IEnumerable<string> paths = targetId is not null
            ? [$"/lights/{targetId}/state"]
            : Opts.LightIds.Count > 0
                ? Opts.LightIds.Select(id => $"/lights/{id}/state")
                : ["/groups/0/action"];

        var json = JsonSerializer.Serialize(state);
        foreach (var path in paths)
        {
            var body = await SendAsync(() => http.PutAsync(BaseUrl + path,
                new StringContent(json, Encoding.UTF8, "application/json")), path);
            ThrowOnHueError(body, path);
        }
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        var body = await SendAsync(() => http.GetAsync(BaseUrl + path), path);
        ThrowOnHueError(body, path);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<string> SendAsync(Func<Task<HttpResponseMessage>> send, string path)
    {
        try
        {
            using var response = await send();
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HueException($"Hue Bridge returned {(int)response.StatusCode} for {path}: {body}");
            return body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new HueException($"Hue Bridge unreachable at {Opts.BridgeIp} — check the bridge IP and your network. ({ex.Message})");
        }
    }

    // The bridge answers 200 even on failures; errors come back as [{"error":{...}}] items.
    private static void ThrowOnHueError(string body, string path)
    {
        if (!body.StartsWith('[') || !body.Contains("\"error\"")) return;
        using var doc = JsonDocument.Parse(body);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("error", out var error))
            {
                var description = error.TryGetProperty("description", out var d) ? d.GetString() : body;
                throw new HueException($"Hue Bridge error for {path}: {description}");
            }
        }
    }
}

public class HueException(string message) : Exception(message);
