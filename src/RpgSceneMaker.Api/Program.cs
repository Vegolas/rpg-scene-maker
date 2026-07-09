using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Endpoints;
using RpgSceneMaker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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
            HueException => (StatusCodes.Status502BadGateway, "Philips Hue error"),
            SpotifyException => (StatusCodes.Status502BadGateway, "Spotify error"),
            HttpRequestException or TaskCanceledException =>
                (StatusCodes.Status502BadGateway, "Spotify unreachable — check the internet connection"),
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
         path.StartsWithSegments("/music") || path.StartsWithSegments("/setup"));
});

// The Blazor WASM control panel is served from this same process.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/health", () => new { status = "ok" });

// API routes, grouped by area (see the Endpoints/ folder). Command endpoints accept GET and POST.
app.MapSceneEndpoints();
app.MapLightEndpoints();
app.MapMusicEndpoints();
app.MapSetupEndpoints();

// Everything that isn't an API route is the Blazor control panel.
app.MapFallbackToFile("index.html");

app.Run();
