using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;

namespace AmbientDirector.Api;

/// <summary>
/// Startup helpers for the friendly console experience of the installable Windows build (issue #75):
/// the URLs shown in the launch banner and used for the browser auto-open, plus the running build's
/// version (issue #147) shown in the banner and returned by <c>GET /diagnostics</c>.
/// </summary>
public static class StartupInfo
{
    /// <summary>
    /// The running build's version: the <see cref="AssemblyInformationalVersionAttribute"/> stamped by
    /// <c>-p:Version=…</c> at publish (with a <c>+&lt;git-sha&gt;</c> suffix appended by the build), falling
    /// back to the assembly version, then "unknown". Shown in the startup banner and by <c>GET /diagnostics</c>.
    /// </summary>
    public static string AppVersion()
    {
        var asm = typeof(StartupInfo).Assembly;
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString()
               ?? "unknown";
    }

    /// <summary>
    /// The panel URLs to show a user: the loopback URL to open on this PC, and a best-guess LAN URL to
    /// open from a tablet/phone on the same Wi-Fi (null when no private IPv4 is found). The port is taken
    /// from the bound server addresses, falling back to the configured <c>Urls</c>, then 5252.
    /// </summary>
    public static (string LocalUrl, string? LanUrl) PanelUrls(IEnumerable<string>? serverAddresses, string? configuredUrls)
    {
        var port = ResolvePort(serverAddresses, configuredUrls);
        var local = $"http://localhost:{port}";
        var ip = BestGuessLanIp();
        return (local, ip is null ? null : $"http://{ip}:{port}");
    }

    private static int ResolvePort(IEnumerable<string>? serverAddresses, string? configuredUrls)
    {
        foreach (var addr in serverAddresses ?? [])
            if (TryPort(addr, out var p)) return p;
        foreach (var addr in (configuredUrls ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (TryPort(addr, out var p)) return p;
        return 5252;
    }

    // Bound addresses look like "http://0.0.0.0:5252", "http://[::]:5252" or "http://+:5252"; normalise the
    // wildcard hosts so Uri can parse out the port.
    private static bool TryPort(string address, out int port)
    {
        port = 0;
        var normalised = address.Replace("*", "0.0.0.0").Replace("+", "0.0.0.0");
        if (Uri.TryCreate(normalised, UriKind.Absolute, out var uri) && uri.Port > 0)
        {
            port = uri.Port;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The first operational, non-loopback private IPv4 address (10/8, 172.16/12, 192.168/16), so the
    /// banner can suggest the address a tablet on the same Wi-Fi should open. Best-effort — returns null
    /// (banner falls back to localhost only) if enumeration fails or nothing private is found.
    /// </summary>
    public static string? BestGuessLanIp()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IsPrivate(ua.Address.GetAddressBytes())) return ua.Address.ToString();
                }
            }
        }
        catch { /* best-effort */ }
        return null;
    }

    private static bool IsPrivate(byte[] b) =>
        b[0] == 10 ||
        (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
        (b[0] == 192 && b[1] == 168);
}
