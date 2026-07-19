using System.Security.Cryptography;
using System.Text;
using AmbientDirector.Api.Contracts;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Validation;

namespace AmbientDirector.Api.Endpoints;

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
                throw new ValidationException("error.setup.sectionsRequired");
            if (config.Provider.ToLowerInvariant() is not ("tuya" or "hue"))
                throw new ValidationException("error.setup.provider");
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
                throw new ValidationException("error.setup.clientIdRequired");
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
                throw new NotConfiguredException("error.notConfigured.spotifyClientId");

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
                throw new ValidationException("error.setup.loginExpired");
            if (string.IsNullOrEmpty(code))
                throw new ValidationException("error.setup.noAuthCode");

            await spotify.ExchangeCodeAsync(code, entry.RedirectUri, entry.Verifier);
            return Results.Redirect("/settings?spotify=connected");
        });

        setup.MapGet("/spotify/devices", (SpotifyClient spotify) => spotify.GetDevicesAsync());

        setup.MapMethods("/spotify/disconnect", EndpointHelpers.GetOrPost, (SpotifyStore store) =>
        {
            store.Disconnect();
            return new { spotify = "disconnected" };
        });

        // In-panel assistant (BYOK) config. The API key is write-only: it is stored server-side and used to
        // talk to the chosen AI provider, but never returned — only the provider, "configured" flag and model
        // come back.
        setup.MapGet("/assistant/config", (AssistantStore store) =>
            new { provider = store.Current.Provider, configured = store.Current.IsConfigured, model = store.Current.Model });

        setup.MapPut("/assistant/config", (AssistantConfigInput input, AssistantStore store) =>
        {
            store.Save(input.Provider, input.ApiKey, input.Model);
            var c = store.Current;
            return Results.Ok(new { provider = c.Provider, configured = c.IsConfigured, model = c.Model });
        });

        setup.MapMethods("/assistant/disconnect", EndpointHelpers.GetOrPost, (AssistantStore store) =>
        {
            store.Clear();
            var c = store.Current;
            return new { provider = c.Provider, configured = c.IsConfigured, model = c.Model };
        });

        // Freesound.org (BYOK, token-only). The API token is write-only: stored server-side and sent to
        // Freesound to search + download HQ previews, but never returned — only the "configured" flag.
        setup.MapGet("/freesound/config", (FreesoundStore store) =>
            new { configured = store.Current.IsConfigured });

        setup.MapPut("/freesound/config", (FreesoundConfigInput input, FreesoundStore store) =>
        {
            if (string.IsNullOrWhiteSpace(input.ApiKey))
                throw new ValidationException("error.setup.freesoundKeyRequired");
            store.Save(input.ApiKey);
            return Results.Ok(new { configured = store.Current.IsConfigured });
        });

        setup.MapMethods("/freesound/disconnect", EndpointHelpers.GetOrPost, (FreesoundStore store) =>
        {
            store.Clear();
            return new { configured = store.Current.IsConfigured };
        });

        // First-run onboarding wizard state (issue #75). `show` drives whether the panel pops the guided
        // setup overlay; the extra flags let the wizard pre-mark steps already done (e.g. an existing install
        // that already connected Spotify). Reading also AUTO-COMPLETES for existing installs: if lights or
        // music are already set up but the flag is still null (an upgrade), stamp it now so long-time users
        // never see the wizard — only a genuinely fresh, unconfigured install gets show=true.
        setup.MapGet("/onboarding", async (SettingsStore settings, SpotifyStore spotify,
            MusicTrackStore musicTracks, AssistantStore assistant, FreesoundStore freesound) =>
        {
            var cfg = settings.Current;
            var lightsConfigured = cfg.Provider.Equals("hue", StringComparison.OrdinalIgnoreCase)
                ? !string.IsNullOrWhiteSpace(cfg.Hue.BridgeIp) && !string.IsNullOrWhiteSpace(cfg.Hue.AppKey)
                : cfg.Tuya.IsConfigured;
            var spotifyConnected = spotify.Current.IsConnected;
            var localMusicAvailable = await musicTracks.AnyAsync();
            var assistantConfigured = assistant.Current.IsConfigured;
            var freesoundConfigured = freesound.Current.IsConfigured;

            if (cfg.OnboardingDoneUtc is null && (lightsConfigured || spotifyConnected || localMusicAvailable))
                settings.MarkOnboardingDone();

            return new
            {
                show = settings.Current.OnboardingDoneUtc is null,
                lightsConfigured,
                spotifyConnected,
                localMusicAvailable,
                assistantConfigured,
                freesoundConfigured,
            };
        });

        // Finish (or skip) the wizard: stamp onboarding done so it never shows again. GET+POST like the other
        // command endpoints. The "Run setup wizard" re-entry button reopens the overlay locally and does NOT
        // hit this — the flag stays set once done.
        setup.MapMethods("/onboarding/done", EndpointHelpers.GetOrPost, (SettingsStore settings) =>
        {
            settings.MarkOnboardingDone();
            return new { show = false };
        });
    }

    // Spotify's dashboard only accepts plain-http redirect URIs on 127.0.0.1 (not "localhost"), and the
    // URI must match character for character. Normalising loopback hosts means connecting from
    // http://localhost:5252 works too — the callback still reaches this same server.
    internal static string SpotifyRedirectUri(HttpRequest request)
    {
        var host = request.Host;
        if (host.Host is "localhost" or "[::1]")
            host = host.Port is { } port ? new HostString("127.0.0.1", port) : new HostString("127.0.0.1");
        return $"{request.Scheme}://{host}/setup/spotify/callback";
    }

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
