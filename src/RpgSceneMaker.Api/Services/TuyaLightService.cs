using System.Globalization;
using com.clusterrr.TuyaNet;
using RpgSceneMaker.Api.Data;

namespace RpgSceneMaker.Api.Services;

/// <summary>
/// Controls a single Tuya RGB+CCT bulb over the local network.
/// Commands are serialized because the bulb only handles one TCP session at a time.
/// </summary>
public class TuyaLightService(SettingsStore settings, ILogger<TuyaLightService> logger) : ILightService
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private sealed record DpProfile(int Switch, int Mode, int Brightness, int Temperature, int Color);

    private static readonly DpProfile V2 = new(20, 21, 22, 23, 24);
    private static readonly DpProfile V1 = new(1, 2, 3, 4, 5);

    private TuyaConfig Opts => settings.Current.Tuya;
    private DpProfile Profile => Opts.DpProfile.Equals("v1", StringComparison.OrdinalIgnoreCase) ? V1 : V2;
    private bool IsV1 => Profile == V1;

    // targetId / transitionMs are Hue concepts; the single Tuya bulb ignores them.
    public async Task SetPowerAsync(bool on, string? targetId = null, int? transitionMs = null) =>
        await SendAsync(new() { [Profile.Switch] = on });

    public async Task<bool> ToggleAsync()
    {
        var dps = await QueryAsync();
        var isOn = dps.TryGetValue(Profile.Switch, out var v) && ParseBool(v);
        await SendAsync(new() { [Profile.Switch] = !isOn });
        return !isOn;
    }

    /// <summary>Switch to colour mode. Brightness percent (0-100) is baked into the HSV value.</summary>
    public async Task SetColorAsync(string hexColor, int? brightnessPercent = null, string? targetId = null, int? transitionMs = null)
    {
        var (r, g, b) = ColorMath.ParseHexColor(hexColor);
        var (h, s, v) = ColorMath.RgbToHsv(r, g, b);
        if (brightnessPercent is int bp)
            v = Math.Clamp(bp / 100.0, 0.01, 1.0);

        await SendAsync(new()
        {
            [Profile.Switch] = true,
            [Profile.Mode] = "colour",
            [Profile.Color] = EncodeColour(h, s, v),
        });
    }

    /// <summary>Switch to white mode. Temperature 0 (warm) - 100 (cold).</summary>
    public async Task SetWhiteAsync(int brightnessPercent, int? temperaturePercent = null, string? targetId = null, int? transitionMs = null)
    {
        var dps = new Dictionary<int, object>
        {
            [Profile.Switch] = true,
            [Profile.Mode] = "white",
            [Profile.Brightness] = ScaleBrightness(brightnessPercent),
        };
        if (temperaturePercent is int tp)
            dps[Profile.Temperature] = IsV1
                ? (int)Math.Round(Math.Clamp(tp, 0, 100) * 255.0 / 100)
                : (int)Math.Round(Math.Clamp(tp, 0, 100) * 1000.0 / 100);

        await SendAsync(dps);
    }

    /// <summary>Set brightness (0-100) without changing mode or hue.</summary>
    public async Task SetBrightnessAsync(int percent, string? targetId = null, int? transitionMs = null)
    {
        var dps = await QueryAsync();
        var mode = dps.TryGetValue(Profile.Mode, out var m) ? m?.ToString() : null;

        if (mode == "colour" && dps.TryGetValue(Profile.Color, out var c) && c?.ToString() is string colour)
        {
            var (h, s, _) = DecodeColour(colour);
            await SendAsync(new()
            {
                [Profile.Switch] = true,
                [Profile.Color] = EncodeColour(h, s, Math.Clamp(percent / 100.0, 0.01, 1.0)),
            });
        }
        else
        {
            await SendAsync(new()
            {
                [Profile.Switch] = true,
                [Profile.Brightness] = ScaleBrightness(percent),
            });
        }
    }

    /// <summary>Raw data points, e.g. to figure out whether the bulb is a v1 or v2 layout.</summary>
    public async Task<object> GetStatusAsync()
    {
        var dps = await QueryAsync();
        return dps.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
    }

    private TuyaDevice CreateDevice()
    {
        if (!Opts.IsConfigured)
            throw new InvalidOperationException(
                "Tuya bulb is not configured. Set the IP, device id and local key on the Settings page (or PUT /setup/config). " +
                "Use GET /setup/scan to find the IP and GET /setup/local-keys to get the local key (see README).");

        var version = Opts.ProtocolVersion.Trim() == "3.1" ? TuyaProtocolVersion.V31 : TuyaProtocolVersion.V33;
        return new TuyaDevice(ip: Opts.Ip, localKey: Opts.LocalKey, deviceId: Opts.DeviceId, protocolVersion: version);
    }

    private async Task<Dictionary<int, object>> QueryAsync()
    {
        await _lock.WaitAsync();
        try
        {
            using var device = CreateDevice();
            return await device.GetDpsAsync();
        }
        finally { _lock.Release(); }
    }

    private async Task SendAsync(Dictionary<int, object> dps)
    {
        await _lock.WaitAsync();
        try
        {
            using var device = CreateDevice();
            logger.LogInformation("Tuya {Ip}: setting DPs {Dps}", Opts.Ip,
                string.Join(", ", dps.Select(kv => $"{kv.Key}={kv.Value}")));
            await device.SetDpsAsync(dps, allowEmptyResponse: true);
        }
        finally { _lock.Release(); }
    }

    internal int ScaleBrightness(int percent)
    {
        var p = Math.Clamp(percent, 1, 100);
        return IsV1
            ? 25 + (int)Math.Round((255 - 25) * p / 100.0)
            : 10 + (int)Math.Round((1000 - 10) * p / 100.0);
    }

    // v2 colour: hhhhssssvvvv (h 0-360, s/v 0-1000, all hex)
    // v1 colour: rrggbbhhhhssvv (rgb hex + h 0-360 hex + s/v 0-255 hex)
    internal string EncodeColour(double h, double s, double v)
    {
        var hue = (int)Math.Round(h) % 360;
        if (IsV1)
        {
            var (r, g, b) = ColorMath.HsvToRgb(h, s, v);
            var s255 = (int)Math.Round(s * 255);
            var v255 = (int)Math.Round(v * 255);
            return $"{r:x2}{g:x2}{b:x2}{hue:x4}{s255:x2}{v255:x2}";
        }
        var s1000 = (int)Math.Round(s * 1000);
        var v1000 = (int)Math.Round(v * 1000);
        return $"{hue:x4}{s1000:x4}{v1000:x4}";
    }

    internal (double h, double s, double v) DecodeColour(string colour)
    {
        try
        {
            if (IsV1 && colour.Length >= 14)
                return (Hex(colour, 6, 4), Hex(colour, 10, 2) / 255.0, Hex(colour, 12, 2) / 255.0);
            if (!IsV1 && colour.Length >= 12)
                return (Hex(colour, 0, 4), Hex(colour, 4, 4) / 1000.0, Hex(colour, 8, 4) / 1000.0);
        }
        catch (FormatException) { }
        return (0, 0, 1);

        static int Hex(string s, int start, int len) =>
            int.Parse(s.AsSpan(start, len), NumberStyles.HexNumber);
    }

    private static bool ParseBool(object? value) =>
        value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
}
