using System.Security.Cryptography;
using System.Text;
using RpgSceneMaker.Api.Contracts;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Validation;

namespace RpgSceneMaker.Api.Endpoints;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this WebApplication app)
    {
        var setup = app.MapGroup("/setup");

        // Find the bulb's IP / device id / protocol version on your LAN.
        setup.MapGet("/scan", (int? seconds, TuyaSetupService tuya) => tuya.ScanAsync(seconds ?? 8));

        // Pull local keys from your Tuya IoT cloud project (see README for the walkthrough).
        setup.MapGet("/local-keys", (string accessId, string apiSecret, string deviceId, string? region, TuyaSetupService tuya) =>
            tuya.GetLocalKeysAsync(accessId, apiSecret, deviceId, region ?? "eu"));

        // Read and update the lighting configuration (persisted to the database, applied immediately).
        setup.MapGet("/config", (SettingsStore store) => store.GetDto());

        setup.MapPut("/config", (LightingConfigDto config, SettingsStore store) =>
        {
            if (config.Hue is null || config.Tuya is null || config.Provider is null)
                throw new ArgumentException("Provider, Hue and Tuya sections are all required.");
            if (config.Provider.ToLowerInvariant() is not ("tuya" or "hue"))
                throw new ArgumentException("Provider must be 'tuya' or 'hue'.");
            LightConfigValidation.Validate(config.Lights);
            // Persist the default light with its colour normalised to canonical #RRGGBB.
            config = config with { DefaultLight = LightConfigValidation.ValidateDefault(config.DefaultLight) };
            store.Save(config);
            return Results.Ok(config);
        });

        // Philips Hue: find the bridge, create an app key (press the bridge's link button first), list light ids.
        setup.MapGet("/hue/discover", (HueSetupService hue) => hue.DiscoverAsync());
        setup.MapMethods("/hue/register", EndpointHelpers.GetOrPost, (string bridgeIp, HueSetupService hue) => hue.RegisterAsync(bridgeIp));
        setup.MapGet("/hue/lights", (string? bridgeIp, string? appKey, HueSetupService hue) => hue.GetLightsAsync(bridgeIp, appKey));

        setup.MapGet("/spotify/config", (HttpRequest request, SpotifyStore store) =>
        {
            var c = store.Current;
            return new
            {
                clientId = c.ClientId,
                connected = c.IsConnected,
                preferredDeviceId = c.PreferredDeviceId,
                preferredDeviceName = c.PreferredDeviceName,
                redirectUri = SpotifyRedirectUri(request),
            };
        });

        setup.MapPut("/spotify/config", (SpotifyConfigInput config, SpotifyStore store) =>
        {
            if (string.IsNullOrWhiteSpace(config.ClientId))
                throw new ArgumentException("A Spotify Client ID is required.");
            store.SaveConfig(config.ClientId.Trim());
            if (config.PreferredDeviceId is not null || config.PreferredDeviceName is not null)
                store.SaveDevice(config.PreferredDeviceId ?? "", config.PreferredDeviceName ?? "");
            var c = store.Current;
            return Results.Ok(new { clientId = c.ClientId, connected = c.IsConnected });
        });

        setup.MapGet("/spotify/login", (HttpRequest request, SpotifyStore store, SpotifyAuthState auth) =>
        {
            var config = store.Current;
            if (!config.IsConfigured)
                throw new InvalidOperationException("Set your Spotify Client ID in Settings before connecting.");

            // PKCE: random verifier, S256 challenge, random state.
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var redirectUri = SpotifyRedirectUri(request);
            auth.Add(state, verifier, redirectUri);

            const string scope = "user-read-playback-state user-modify-playback-state playlist-read-private playlist-read-collaborative";
            var authorizeUrl =
                "https://accounts.spotify.com/authorize?response_type=code" +
                $"&client_id={Uri.EscapeDataString(config.ClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                "&code_challenge_method=S256" +
                $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                $"&state={Uri.EscapeDataString(state)}" +
                $"&scope={Uri.EscapeDataString(scope)}";
            return Results.Redirect(authorizeUrl);
        });

        setup.MapGet("/spotify/callback", async (string? code, string? state, string? error,
            SpotifyAuthState auth, SpotifyClient spotify) =>
        {
            if (!string.IsNullOrEmpty(error))
                return Results.Redirect($"/settings?spotify=error:{Uri.EscapeDataString(error)}");
            if (string.IsNullOrEmpty(state) || auth.Take(state) is not { } entry)
                throw new ArgumentException("Login expired or invalid — try connecting again.");
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("Spotify did not return an authorization code.");

            await spotify.ExchangeCodeAsync(code, entry.RedirectUri, entry.Verifier);
            return Results.Redirect("/settings?spotify=connected");
        });

        setup.MapGet("/spotify/devices", (SpotifyClient spotify) => spotify.GetDevicesAsync());

        setup.MapMethods("/spotify/disconnect", EndpointHelpers.GetOrPost, (SpotifyStore store) =>
        {
            store.Disconnect();
            return new { spotify = "disconnected" };
        });
    }

    // Spotify's dashboard only accepts plain-http redirect URIs on 127.0.0.1 (not "localhost"), and the
    // URI must match character for character. Normalising loopback hosts means connecting from
    // http://localhost:5252 works too — the callback still reaches this same server.
    private static string SpotifyRedirectUri(HttpRequest request)
    {
        var host = request.Host;
        if (host.Host is "localhost" or "[::1]")
            host = host.Port is { } port ? new HostString("127.0.0.1", port) : new HostString("127.0.0.1");
        return $"{request.Scheme}://{host}/setup/spotify/callback";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
