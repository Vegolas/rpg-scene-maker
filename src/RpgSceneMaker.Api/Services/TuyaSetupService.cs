using System.Collections.Concurrent;
using com.clusterrr.TuyaNet;
using RpgSceneMaker.Api.Errors;

namespace RpgSceneMaker.Api.Services;

public record DiscoveredDevice(string Ip, string DeviceId, string ProtocolVersion, string? ProductKey);
public record DeviceKeyInfo(string Id, string Name, string? Ip, string LocalKey, string ProductName, bool Online);

/// <summary>One-time setup helpers: find the bulb on the LAN and pull local keys from the Tuya cloud.</summary>
public class TuyaSetupService
{
    /// <summary>Listen for the UDP broadcasts every Tuya device sends and report what shows up.</summary>
    public async Task<List<DiscoveredDevice>> ScanAsync(int seconds)
    {
        var found = new ConcurrentDictionary<string, DiscoveredDevice>();
        var scanner = new TuyaScanner();
        scanner.OnNewDeviceInfoReceived += (_, device) =>
            found.TryAdd(device.GwId, new DiscoveredDevice(device.IP, device.GwId, device.Version, device.ProductKey));

        scanner.Start();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60)));
        }
        finally
        {
            scanner.Stop();
        }
        return [.. found.Values];
    }

    /// <summary>
    /// Fetch local keys for all devices on the Tuya account linked to the given cloud project.
    /// Requires a (free) project on https://iot.tuya.com with the Smart Life app account linked.
    /// </summary>
    public async Task<List<DeviceKeyInfo>> GetLocalKeysAsync(string accessId, string apiSecret, string anyDeviceId, string region)
    {
        var api = new TuyaApi(ParseRegion(region), accessId, apiSecret);
        var devices = await api.GetAllDevicesInfoAsync(anyDeviceId);
        return [.. devices.Select(d => new DeviceKeyInfo(d.Id, d.Name, d.Ip, d.LocalKey, d.ProductName, d.Online))];
    }

    private static TuyaApi.Region ParseRegion(string region) => region.ToLowerInvariant() switch
    {
        "eu" or "centraleurope" => TuyaApi.Region.CentralEurope,
        "eu-w" or "westerneurope" => TuyaApi.Region.WesternEurope,
        "us" or "westernamerica" => TuyaApi.Region.WesternAmerica,
        "us-e" or "easternamerica" => TuyaApi.Region.EasternAmerica,
        "cn" or "china" => TuyaApi.Region.China,
        "in" or "india" => TuyaApi.Region.India,
        _ => throw new ValidationException("error.setup.unknownRegion", region),
    };
}
