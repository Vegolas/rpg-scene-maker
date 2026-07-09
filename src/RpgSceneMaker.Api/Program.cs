using System.Net.Sockets;
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
builder.Services.AddScoped<SceneActivator>();
builder.Services.AddHttpClient<KenkuClient>(client => client.Timeout = TimeSpan.FromSeconds(5));

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
        path.StartsWithSegments("/scenes") || path.StartsWithSegments("/lights") ||
        path.StartsWithSegments("/music") || path.StartsWithSegments("/sfx") ||
        path.StartsWithSegments("/setup");
});

// The Blazor WASM control panel is served from this same process.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

string[] getOrPost = ["GET", "POST"];

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

// ---------- Lights (Tuya bulb) ----------
var lights = app.MapGroup("/lights");

lights.MapMethods("/on", getOrPost, async (ILightService bulb) =>
{
    await bulb.SetPowerAsync(true);
    return new { light = "on" };
});

lights.MapMethods("/off", getOrPost, async (ILightService bulb) =>
{
    await bulb.SetPowerAsync(false);
    return new { light = "off" };
});

lights.MapMethods("/toggle", getOrPost, async (ILightService bulb) =>
    new { light = await bulb.ToggleAsync() ? "on" : "off" });

// /lights/color?hex=FF8C2A&brightness=80
lights.MapMethods("/color", getOrPost, async (string hex, int? brightness, ILightService bulb) =>
{
    await bulb.SetColorAsync(hex, brightness);
    return new { light = "colour", hex, brightness };
});

// /lights/white?brightness=80&temperature=30   (temperature: 0 warm - 100 cold)
lights.MapMethods("/white", getOrPost, async (int? brightness, int? temperature, ILightService bulb) =>
{
    await bulb.SetWhiteAsync(brightness ?? 100, temperature);
    return new { light = "white", brightness = brightness ?? 100, temperature };
});

// /lights/brightness?value=50
lights.MapMethods("/brightness", getOrPost, async (int value, ILightService bulb) =>
{
    await bulb.SetBrightnessAsync(value);
    return new { brightness = value };
});

lights.MapGet("/status", (ILightService bulb) => bulb.GetStatusAsync());

// ---------- Music (Kenku FM playlists) ----------
var music = app.MapGroup("/music");

music.MapMethods("/play", getOrPost, async (string id, KenkuClient kenku) =>
{
    await kenku.PlayAsync(id);
    return new { playing = id };
});

music.MapMethods("/pause", getOrPost, async (KenkuClient kenku) => { await kenku.PauseAsync(); return new { music = "paused" }; });
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
    store.Save(config);
    return Results.Ok(config);
});

// Philips Hue: find the bridge, create an app key (press the bridge's link button first), list light ids.
setup.MapGet("/hue/discover", (HueSetupService hue) => hue.DiscoverAsync());
setup.MapMethods("/hue/register", getOrPost, (string bridgeIp, HueSetupService hue) => hue.RegisterAsync(bridgeIp));
setup.MapGet("/hue/lights", (string? bridgeIp, string? appKey, HueSetupService hue) => hue.GetLightsAsync(bridgeIp, appKey));

// Everything that isn't an API route is the Blazor control panel.
app.MapFallbackToFile("index.html");

app.Run();
