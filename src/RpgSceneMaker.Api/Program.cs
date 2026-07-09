using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Models;
using RpgSceneMaker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KenkuOptions>(builder.Configuration.GetSection(KenkuOptions.Section));

// Scenes and lighting settings live in SQLite; Database:Path overrides the default location.
var dbPath = builder.Configuration["Database:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RpgSceneMaker", "rpg-scene-maker.db");
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<TuyaLightService>();
builder.Services.AddSingleton<TuyaSetupService>();
builder.Services.AddHttpClient<HueLightService>(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHttpClient<HueSetupService>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<SceneStore>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<CurrentState>();
builder.Services.AddSingleton<LightRegistry>();
builder.Services.AddSingleton<EffectEngine>();
builder.Services.AddScoped<SceneActivator>();
builder.Services.AddHttpClient<KenkuClient>(client => client.Timeout = TimeSpan.FromSeconds(5));

// Spotify: cloud Web API (PKCE) to control a Spotify Connect device on the LAN.
builder.Services.AddSingleton<SpotifyStore>();
builder.Services.AddSingleton<SpotifyTokenCache>();
builder.Services.AddSingleton<SpotifyAuthState>();
builder.Services.AddHttpClient<SpotifyClient>(client => client.Timeout = TimeSpan.FromSeconds(10));

// The configured provider picks which system scenes and /lights control ("tuya" or "hue").
builder.Services.AddScoped<ILightService>(sp =>
    sp.GetRequiredService<SettingsStore>().Current.Provider
        .Equals("hue", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<HueLightService>()
        : sp.GetRequiredService<TuyaLightService>());

var app = builder.Build();

// Create/upgrade the database, then pull in data from the legacy JSON files on first run.
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
    using var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    db.Database.Migrate();
    LegacyImporter.Run(db, app.Configuration, app.Environment, app.Logger);
}

// Map integration failures to useful status codes so a failing Stream Deck button tells you why.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        var (status, title) = ex switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            InvalidOperationException => (StatusCodes.Status503ServiceUnavailable, "Not configured"),
            KenkuException => (StatusCodes.Status502BadGateway, "Kenku FM error"),
            HueException => (StatusCodes.Status502BadGateway, "Philips Hue error"),
            SpotifyException => (StatusCodes.Status502BadGateway, "Spotify error"),
            HttpRequestException or TaskCanceledException =>
                (StatusCodes.Status502BadGateway, "Kenku FM unreachable — is Kenku FM running with Remote enabled (Settings > Remote)?"),
            SocketException or IOException or TimeoutException =>
                (StatusCodes.Status504GatewayTimeout, "Bulb unreachable — check Tuya:Ip and that the bulb is powered"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };
        await Results.Problem(title: title, detail: ex.Message, statusCode: status).ExecuteAsync(context);
    }
});

// Optional shared-secret check for when the API listens on the whole LAN.
// Enabled by setting Security:ApiKey; the Blazor UI sends it as an X-Api-Key header,
// and Stream-Deck-style callers can append ?apiKey=... instead.
app.Use(async (context, next) =>
{
    var requiredKey = app.Configuration["Security:ApiKey"];
    if (!string.IsNullOrEmpty(requiredKey) && IsProtectedPath(context.Request.Path))
    {
        var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault()
                       ?? context.Request.Query["apiKey"].FirstOrDefault();
        if (provided != requiredKey)
        {
            await Results.Problem(title: "Unauthorized",
                detail: "Missing or wrong API key. Send it as an X-Api-Key header or ?apiKey= query parameter.",
                statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            return;
        }
    }
    await next();

    static bool IsProtectedPath(PathString path) =>
        // The Spotify OAuth callback is a top-level browser redirect from Spotify and cannot carry
        // the API key; the opaque state value (validated server-side) guards it instead.
        !path.StartsWithSegments("/setup/spotify/callback") &&
        (path.StartsWithSegments("/scenes") || path.StartsWithSegments("/lights") ||
         path.StartsWithSegments("/music") || path.StartsWithSegments("/sfx") ||
         path.StartsWithSegments("/setup"));
});

// The Blazor WASM control panel is served from this same process.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

string[] getOrPost = ["GET", "POST"];

// Fire a cross-provider pause/stop without letting its failure surface (the other player may be
// off, unconfigured, or have no active device — none of which should fail the primary action).
static async Task BestEffort(Func<Task> action)
{
    try { await action(); }
    catch { /* ignored — best effort */ }
}

app.MapGet("/health", () => new { status = "ok" });

// ---------- Scenes ----------
var scenes = app.MapGroup("/scenes");

scenes.MapGet("/", (SceneStore store) => store.GetAllAsync());

scenes.MapGet("/active", (CurrentState state) =>
    new { id = state.ActiveSceneId, activatedAt = state.ActivatedAt });

scenes.MapGet("/{id}", async (string id, SceneStore store) =>
    await store.GetAsync(id) is { } scene ? Results.Ok(scene) : Results.NotFound());

scenes.MapPut("/{id}", async (string id, Scene scene, SceneStore store) =>
{
    scene.Id = id;
    SceneValidation.Validate(scene);
    await store.UpsertAsync(scene);
    return Results.Ok(scene);
});

scenes.MapDelete("/{id}", async (string id, SceneStore store) =>
    await store.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());

scenes.MapMethods("/{id}/activate", getOrPost, async (string id, SceneStore store, SceneActivator activator) =>
{
    if (await store.GetAsync(id) is not { } scene)
        return Results.NotFound(new { error = $"No scene with id '{id}'. See GET /scenes." });
    var result = await activator.ActivateAsync(scene);
    return Results.Json(result, statusCode: result.FullySucceeded ? 200 : 207);
});

// ---------- Lights ----------
// Manual light control (and scene activation) always ends any running effect loops.
var lights = app.MapGroup("/lights");

lights.MapMethods("/on", getOrPost, async (ILightService bulb, EffectEngine effects) =>
{
    effects.StopAll();
    await bulb.SetPowerAsync(true);
    return new { light = "on" };
});

lights.MapMethods("/off", getOrPost, async (ILightService bulb, EffectEngine effects) =>
{
    effects.StopAll();
    await bulb.SetPowerAsync(false);
    return new { light = "off" };
});

lights.MapMethods("/toggle", getOrPost, async (ILightService bulb, EffectEngine effects) =>
{
    effects.StopAll();
    return new { light = await bulb.ToggleAsync() ? "on" : "off" };
});

// /lights/color?hex=FF8C2A&brightness=80
lights.MapMethods("/color", getOrPost, async (string hex, int? brightness, ILightService bulb, EffectEngine effects) =>
{
    effects.StopAll();
    await bulb.SetColorAsync(hex, brightness);
    return new { light = "colour", hex, brightness };
});

// /lights/white?brightness=80&temperature=30   (temperature: 0 warm - 100 cold)
lights.MapMethods("/white", getOrPost, async (int? brightness, int? temperature, ILightService bulb, EffectEngine effects) =>
{
    effects.StopAll();
    await bulb.SetWhiteAsync(brightness ?? 100, temperature);
    return new { light = "white", brightness = brightness ?? 100, temperature };
});

// /lights/brightness?value=50
lights.MapMethods("/brightness", getOrPost, async (int value, ILightService bulb, EffectEngine effects) =>
{
    effects.StopAll();
    await bulb.SetBrightnessAsync(value);
    return new { brightness = value };
});

lights.MapGet("/status", (ILightService bulb) => bulb.GetStatusAsync());

// Registered lights the per-light endpoints and scenes can target.
lights.MapGet("/list", (LightRegistry registry) =>
    registry.GetAll().Select(l => new { key = l.Key, name = l.Name, provider = l.Provider }));

// ---- Per-light manual control (by registry key) ----
lights.MapMethods("/{key}/on", getOrPost, async (string key, LightRegistry registry, EffectEngine effects) =>
{
    effects.StopAll();
    var r = registry.Resolve(key);
    await r.Service.SetPowerAsync(true, r.TargetId);
    return new { key, light = "on" };
});

lights.MapMethods("/{key}/off", getOrPost, async (string key, LightRegistry registry, EffectEngine effects) =>
{
    effects.StopAll();
    var r = registry.Resolve(key);
    await r.Service.SetPowerAsync(false, r.TargetId);
    return new { key, light = "off" };
});

// /lights/{key}/color?hex=FF8C2A&brightness=80
lights.MapMethods("/{key}/color", getOrPost, async (string key, string hex, int? brightness, LightRegistry registry, EffectEngine effects) =>
{
    effects.StopAll();
    var r = registry.Resolve(key);
    await r.Service.SetColorAsync(hex, brightness, r.TargetId);
    return new { key, light = "colour", hex, brightness };
});

// /lights/{key}/white?brightness=80&temperature=30
lights.MapMethods("/{key}/white", getOrPost, async (string key, int? brightness, int? temperature, LightRegistry registry, EffectEngine effects) =>
{
    effects.StopAll();
    var r = registry.Resolve(key);
    await r.Service.SetWhiteAsync(brightness ?? 100, temperature, r.TargetId);
    return new { key, light = "white", brightness = brightness ?? 100, temperature };
});

// /lights/{key}/brightness?value=50
lights.MapMethods("/{key}/brightness", getOrPost, async (string key, int value, LightRegistry registry, EffectEngine effects) =>
{
    effects.StopAll();
    var r = registry.Resolve(key);
    await r.Service.SetBrightnessAsync(value, r.TargetId);
    return new { key, brightness = value };
});

// ---------- Music (Kenku FM playlists) ----------
var music = app.MapGroup("/music");

music.MapMethods("/play", getOrPost, async (string id, KenkuClient kenku, SpotifyClient spotify) =>
{
    if (SpotifyClient.IsSpotifyUri(id))
    {
        await spotify.PlayAsync(id);
        await BestEffort(kenku.PauseAsync); // stop Kenku if it happens to be running
    }
    else
    {
        await kenku.PlayAsync(id);
        await BestEffort(() => spotify.PauseAsync(throwOnNoDevice: false)); // stop Spotify if connected
    }
    return new { playing = id };
});

music.MapMethods("/pause", getOrPost, async (KenkuClient kenku, SpotifyClient spotify, SpotifyStore spotifyStore) =>
{
    // With Spotify connected, Kenku may legitimately be off — pause it best-effort only.
    if (spotifyStore.Current.IsConnected)
    {
        await BestEffort(kenku.PauseAsync);
        await spotify.PauseAsync(throwOnNoDevice: false);
    }
    else
    {
        await kenku.PauseAsync();
    }
    return new { music = "paused" };
});
music.MapMethods("/resume", getOrPost, async (KenkuClient kenku) => { await kenku.ResumeAsync(); return new { music = "playing" }; });
music.MapMethods("/next", getOrPost, async (KenkuClient kenku) => { await kenku.NextAsync(); return new { music = "next" }; });
music.MapMethods("/previous", getOrPost, async (KenkuClient kenku) => { await kenku.PreviousAsync(); return new { music = "previous" }; });

// /music/volume?value=0.5
music.MapMethods("/volume", getOrPost, async (double value, KenkuClient kenku) =>
{
    await kenku.SetVolumeAsync(value);
    return new { volume = value };
});

music.MapMethods("/mute", getOrPost, async (bool? value, KenkuClient kenku) =>
{
    await kenku.SetMuteAsync(value ?? true);
    return new { mute = value ?? true };
});

music.MapMethods("/shuffle", getOrPost, async (bool? value, KenkuClient kenku) =>
{
    await kenku.SetShuffleAsync(value ?? true);
    return new { shuffle = value ?? true };
});

// /music/repeat?mode=playlist   (off | track | playlist)
music.MapMethods("/repeat", getOrPost, async (string mode, KenkuClient kenku) =>
{
    if (mode is not ("off" or "track" or "playlist"))
        throw new ArgumentException("mode must be one of: off, track, playlist");
    await kenku.SetRepeatAsync(mode);
    return new { repeat = mode };
});

music.MapGet("/playlists", (KenkuClient kenku) => kenku.GetPlaylistsAsync());
music.MapGet("/state", (KenkuClient kenku) => kenku.GetPlaylistStateAsync());

// Spotify (cloud Web API) — playlists, track search and current playback state.
music.MapGet("/spotify/playlists", (SpotifyClient spotify) => spotify.GetPlaylistsAsync());
music.MapGet("/spotify/search", (string q, SpotifyClient spotify) =>
{
    if (string.IsNullOrWhiteSpace(q))
        throw new ArgumentException("Provide a search term with ?q=");
    return spotify.SearchTracksAsync(q);
});
music.MapGet("/spotify/state", (SpotifyClient spotify) => spotify.GetStateAsync());

// ---------- Sound effects (Kenku FM soundboard) ----------
var sfx = app.MapGroup("/sfx");

sfx.MapMethods("/play", getOrPost, async (string id, KenkuClient kenku) =>
{
    await kenku.PlaySoundAsync(id);
    return new { playing = id };
});

sfx.MapMethods("/stop", getOrPost, async (string id, KenkuClient kenku) =>
{
    await kenku.StopSoundAsync(id);
    return new { stopped = id };
});

sfx.MapGet("/sounds", (KenkuClient kenku) => kenku.GetSoundboardsAsync());
sfx.MapGet("/state", (KenkuClient kenku) => kenku.GetSoundboardStateAsync());

// ---------- One-time setup helpers ----------
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
    store.Save(config);
    return Results.Ok(config);
});

// Philips Hue: find the bridge, create an app key (press the bridge's link button first), list light ids.
setup.MapGet("/hue/discover", (HueSetupService hue) => hue.DiscoverAsync());
setup.MapMethods("/hue/register", getOrPost, (string bridgeIp, HueSetupService hue) => hue.RegisterAsync(bridgeIp));
setup.MapGet("/hue/lights", (string? bridgeIp, string? appKey, HueSetupService hue) => hue.GetLightsAsync(bridgeIp, appKey));

// Spotify: Client-ID config + Authorization Code (PKCE) connect flow.
static string SpotifyRedirectUri(HttpRequest request) =>
    $"{request.Scheme}://{request.Host}/setup/spotify/callback";

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

setup.MapMethods("/spotify/disconnect", getOrPost, (SpotifyStore store) =>
{
    store.Disconnect();
    return new { spotify = "disconnected" };
});

static string Base64Url(byte[] bytes) =>
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

// Everything that isn't an API route is the Blazor control panel.
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Guards a scene coming from the editor before it reaches the store; failures map to HTTP 400.</summary>
static class SceneValidation
{
    public static void Validate(Scene scene)
    {
        if (string.IsNullOrWhiteSpace(scene.Id))
            throw new ArgumentException("Scene id is required.");
        if (!scene.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Scene id may only contain letters, digits, '-' and '_'.");
        if (string.IsNullOrWhiteSpace(scene.Name))
            throw new ArgumentException("Scene name is required.");

        if (scene.Light is { } light)
        {
            if (light.Brightness is < 0 or > 100)
                throw new ArgumentException("Light brightness must be between 0 and 100.");
            if (light.Temperature is < 0 or > 100)
                throw new ArgumentException("Light temperature must be between 0 and 100.");
            if (light.Color is not null)
                light.Color = LightValidation.NormalizeHex(light.Color);
        }

        // JSON "lights": null / "colors": null / "soundEffects": null overwrite the C# defaults.
        scene.Lights ??= [];
        scene.SoundEffects ??= [];

        foreach (var entry in scene.Lights)
        {
            if (string.IsNullOrWhiteSpace(entry.LightKey) || !LightValidation.IsSlug(entry.LightKey))
                throw new ArgumentException("Each scene light needs a LightKey slug ([a-z0-9-_]).");
            if (entry.Brightness is < 0 or > 100)
                throw new ArgumentException($"Light '{entry.LightKey}' brightness must be between 0 and 100.");
            if (entry.Temperature is < 0 or > 100)
                throw new ArgumentException($"Light '{entry.LightKey}' temperature must be between 0 and 100.");
            if (entry.Color is not null)
                entry.Color = LightValidation.NormalizeHex(entry.Color);

            if (entry.Effect is { } fx)
            {
                fx.Colors ??= [];
                if (fx.Type is not ("flicker" or "glow" or "storm" or "drift"))
                    throw new ArgumentException($"Unknown effect type '{fx.Type}' on light '{entry.LightKey}'. Use flicker, glow, storm or drift.");
                if (fx.Speed is < 1 or > 10)
                    throw new ArgumentException($"Effect speed on light '{entry.LightKey}' must be between 1 and 10.");
                if (fx.Intensity is < 1 or > 10)
                    throw new ArgumentException($"Effect intensity on light '{entry.LightKey}' must be between 1 and 10.");
                for (var i = 0; i < fx.Colors.Count; i++)
                    fx.Colors[i] = LightValidation.NormalizeHex(fx.Colors[i]);
                if (fx.Type == "drift" && fx.Colors.Count < 2)
                    throw new ArgumentException($"The 'drift' effect on light '{entry.LightKey}' needs at least 2 colors.");
            }
        }

        if (scene.Music is { Volume: { } volume } && volume is < 0.0 or > 1.0)
            throw new ArgumentException("Music volume must be between 0.0 and 1.0.");
    }
}

/// <summary>Guards the light registry coming from the Settings page before it reaches the store.</summary>
static class LightConfigValidation
{
    public static void Validate(List<RegisteredLightDto>? lights)
    {
        if (lights is null) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lights)
        {
            if (string.IsNullOrWhiteSpace(l.Key) || !LightValidation.IsSlug(l.Key))
                throw new ArgumentException($"Light key '{l.Key}' must be a non-empty slug ([a-z0-9-_]).");
            if (!seen.Add(l.Key))
                throw new ArgumentException($"Duplicate light key '{l.Key}'. Keys must be unique (case-insensitive).");
            if (l.Provider?.ToLowerInvariant() is not ("tuya" or "hue"))
                throw new ArgumentException($"Light '{l.Key}' provider must be 'tuya' or 'hue'.");
            if (l.Provider.Equals("hue", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(l.HueId))
                throw new ArgumentException($"Hue light '{l.Key}' needs a HueId.");
        }
    }
}

/// <summary>Shared light-related validation helpers.</summary>
static class LightValidation
{
    public static bool IsSlug(string s) => s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // Accept #RGB or #RRGGBB (leading # optional) and store the canonical "#RRGGBB" the light services parse.
    public static string NormalizeHex(string raw)
    {
        var s = raw.Trim().TrimStart('#');
        if (s.Length == 3 && s.All(Uri.IsHexDigit))
            s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length != 6 || !s.All(Uri.IsHexDigit))
            throw new ArgumentException($"'{raw}' is not a valid hex color. Use #RGB or #RRGGBB, e.g. #FF8C2A.");
        return "#" + s.ToUpperInvariant();
    }
}

// Body of PUT /setup/spotify/config (device fields optional — omitted keeps the saved value).
record SpotifyConfigInput(string ClientId, string? PreferredDeviceId, string? PreferredDeviceName);
