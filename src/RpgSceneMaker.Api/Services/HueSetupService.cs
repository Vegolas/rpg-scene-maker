using System.Text;
using System.Text.Json;

namespace RpgSceneMaker.Api.Services;

public record HueBridge(string Id, string Ip);
public record HueRegistration(string BridgeIp, string AppKey, string Hint);
public record HueLight(string Id, string Name, string Type, bool On, bool Reachable);

/// <summary>One-time Philips Hue setup: find the bridge, create an app key, list lights.</summary>
public class HueSetupService(HttpClient http, SettingsStore settings)
{
    /// <summary>Find Hue Bridges on your network via Philips' discovery endpoint.</summary>
    public async Task<List<HueBridge>> DiscoverAsync()
    {
        var body = await WrapNetworkErrors(
            () => http.GetStringAsync("https://discovery.meethue.com/"),
            "Could not reach discovery.meethue.com — check your internet connection, or find the bridge IP in the Hue app (Settings > My Hue system > bridge)");
        using var doc = JsonDocument.Parse(body);
        return [.. doc.RootElement.EnumerateArray().Select(b => new HueBridge(
            b.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            b.TryGetProperty("internalipaddress", out var ip) ? ip.GetString() ?? "" : ""))];
    }

    private static async Task<T> WrapNetworkErrors<T>(Func<Task<T>> action, string message)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new HueException($"{message}. ({ex.Message})");
        }
    }

    /// <summary>
    /// Create an app key. Press the round link button on the bridge first,
    /// then call this within ~30 seconds.
    /// </summary>
    public async Task<HueRegistration> RegisterAsync(string bridgeIp)
    {
        var content = new StringContent("""{"devicetype":"rpg-scene-maker#pc"}""", Encoding.UTF8, "application/json");
        var body = await WrapNetworkErrors(async () =>
        {
            using var response = await http.PostAsync($"http://{bridgeIp}/api", content);
            return await response.Content.ReadAsStringAsync();
        }, $"Could not reach a Hue Bridge at {bridgeIp}");

        using var doc = JsonDocument.Parse(body);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("success", out var success) &&
                success.TryGetProperty("username", out var username))
            {
                return new HueRegistration(bridgeIp, username.GetString() ?? "",
                    "Save this as the Hue app key (and the ip as the bridge IP) on the Settings page or via PUT /setup/config.");
            }
            if (item.TryGetProperty("error", out var error) &&
                error.TryGetProperty("type", out var type) && type.GetInt32() == 101)
            {
                throw new HueException(
                    "Link button not pressed. Press the round button on top of the Hue Bridge, " +
                    "then call this endpoint again within 30 seconds.");
            }
        }
        throw new HueException($"Unexpected bridge response: {body}");
    }

    /// <summary>List lights with their ids for Hue:LightIds. Falls back to the configured bridge/key.</summary>
    public async Task<List<HueLight>> GetLightsAsync(string? bridgeIp, string? appKey)
    {
        var ip = bridgeIp ?? settings.Current.Hue.BridgeIp;
        var key = appKey ?? settings.Current.Hue.AppKey;
        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Pass ?bridgeIp=...&appKey=... or save the bridge IP and app key on the Settings page first.");

        var body = await WrapNetworkErrors(
            () => http.GetStringAsync($"http://{ip}/api/{key}/lights"),
            $"Could not reach a Hue Bridge at {ip}");
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            // An array here means the bridge returned an error item instead of the lights map.
            throw new HueException($"Hue Bridge error: {doc.RootElement.GetRawText()}");
        }

        return [.. doc.RootElement.EnumerateObject().Select(p => new HueLight(
            p.Name,
            p.Value.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            p.Value.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
            p.Value.TryGetProperty("state", out var s) && s.TryGetProperty("on", out var on) && on.GetBoolean(),
            p.Value.TryGetProperty("state", out var s2) && s2.TryGetProperty("reachable", out var r) && r.GetBoolean()))];
    }
}
