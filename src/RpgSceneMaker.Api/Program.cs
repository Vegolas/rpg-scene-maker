using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Endpoints;
using RpgSceneMaker.Api.Logging;
using RpgSceneMaker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Scenes and lighting settings live in SQLite; Database:Path overrides the default location.
var dbPath = builder.Configuration["Database:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RpgSceneMaker", "rpg-scene-maker.db");
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

// Sound-effect audio files live next to the database; Sounds:Path overrides the location.
var soundsPath = builder.Configuration["Sounds:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RpgSceneMaker", "sounds");

// Full-art tile background images live next to the database; Images:Path overrides the location.
var imagesPath = builder.Configuration["Images:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RpgSceneMaker", "images");

// Startup-captured facts surfaced by GET /diagnostics (developer mode): process start time plus the
// resolved on-disk paths, so the endpoint reuses the exact values instead of re-resolving them.
builder.Services.AddSingleton(new DiagnosticsInfo(DateTimeOffset.UtcNow, dbPath, soundsPath));

builder.Services.AddSingleton<TuyaLightService>();
builder.Services.AddSingleton<TuyaSetupService>();
builder.Services.AddHttpClient<HueLightService>(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHttpClient<HueSetupService>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<SceneStore>();

// Soundboard: metadata in SQLite, audio files on disk, playback on the server's own audio device.
builder.Services.AddSingleton<SoundStore>();
builder.Services.AddSingleton(new SoundFileStorage(soundsPath));
builder.Services.AddSingleton<SoundboardPlayer>();

// Full-art tile backgrounds: uploaded via /images, stored on disk, referenced by stored file name.
builder.Services.AddSingleton(new ImageFileStorage(imagesPath));

builder.Services.AddSingleton<EventStore>();
builder.Services.AddSingleton<ScreenStore>();
builder.Services.AddSingleton<LightFxStore>();

builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<CurrentState>();
builder.Services.AddSingleton<LightRegistry>();
builder.Services.AddSingleton<EffectEngine>();
builder.Services.AddScoped<SceneLightApplier>();
builder.Services.AddScoped<SceneActivator>();
builder.Services.AddScoped<EventAfterApplier>();
builder.Services.AddScoped<EventActivator>();
// Runs an event's background timeline; creates its own scope per run (ILightService is scoped).
builder.Services.AddSingleton<EventTimelineRunner>();
// Bounded preview of a library Light FX; like the timeline runner it owns a scope per test run.
builder.Services.AddSingleton<LightFxTester>();

// Spotify: cloud Web API (PKCE) to control a Spotify Connect device on the LAN.
builder.Services.AddSingleton<SpotifyStore>();
builder.Services.AddSingleton<SpotifyTokenCache>();
builder.Services.AddSingleton<SpotifyAuthState>();
builder.Services.AddHttpClient<SpotifyClient>(client => client.Timeout = TimeSpan.FromSeconds(10));

// Anthropic: bring-your-own-key settings for the in-panel AI assistant (key + model, stored in SQLite).
builder.Services.AddSingleton<AnthropicStore>();

// Shared AI tool layer over scenes/events/light FX (+ read-only context and live control), consumed by the
// MCP server and the in-panel assistant (both added in later commits).
builder.Services.AddSingleton<RpgSceneMaker.Api.Services.Ai.AiToolService>();

// MCP server hosted in-process at /mcp (streamable HTTP, stateless — MCP clients resend the API key on every
// request, so there is no session to keep). The four tool-type classes are thin adapters over AiToolService.
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<RpgSceneMaker.Api.Services.Ai.SceneMcpTools>()
    .WithTools<RpgSceneMaker.Api.Services.Ai.EventMcpTools>()
    .WithTools<RpgSceneMaker.Api.Services.Ai.LightFxMcpTools>()
    .WithTools<RpgSceneMaker.Api.Services.Ai.LibraryMcpTools>();

// In-memory log buffer surfaced by the panel's Logs tab. Whitelist our own logs at Information and
// default everything else (EF SQL, HttpClient request chatter, hosting) to Warning+, so the tab stays
// signal rather than framework noise.
builder.Services.AddSingleton<InMemoryLogStore>();
builder.Services.AddSingleton<ILoggerProvider, InMemoryLoggerProvider>();
builder.Logging.AddFilter<InMemoryLoggerProvider>(null, LogLevel.Warning);
builder.Logging.AddFilter<InMemoryLoggerProvider>("RpgSceneMaker", LogLevel.Information);

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
            SoundboardException => (StatusCodes.Status503ServiceUnavailable, "Soundboard error"),
            HttpRequestException or TaskCanceledException =>
                (StatusCodes.Status502BadGateway, "Spotify unreachable — check the internet connection"),
            SocketException or IOException or TimeoutException =>
                (StatusCodes.Status504GatewayTimeout, "Bulb unreachable — check Tuya:Ip and that the bulb is powered"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };
        // Real faults (bulb/Spotify unreachable, unexpected) are errors with a stack; "not configured"
        // (503) and bad requests (4xx) are expected states — warn, and skip the noisy stack trace.
        var isFault = status is >= 500 and not 503;
        app.Logger.Log(isFault ? LogLevel.Error : LogLevel.Warning, isFault ? ex : null,
            "{Method} {Path} → {Status} {Title}: {Detail}",
            context.Request.Method, context.Request.Path, status, title, ex.Message);
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
         path.StartsWithSegments("/music") || path.StartsWithSegments("/sounds") ||
         path.StartsWithSegments("/events") || path.StartsWithSegments("/screens") ||
         path.StartsWithSegments("/lightfx") || path.StartsWithSegments("/images") ||
         path.StartsWithSegments("/setup") || path.StartsWithSegments("/logs") ||
         path.StartsWithSegments("/diagnostics") || path.StartsWithSegments("/mcp"));
});

// The Blazor WASM control panel is served from this same process.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/health", () => new { status = "ok" });

// API routes, grouped by area (see the Endpoints/ folder). Command endpoints accept GET and POST.
app.MapSceneEndpoints();
app.MapLightEndpoints();
app.MapMusicEndpoints();
app.MapSoundEndpoints();
app.MapEventEndpoints();
app.MapScreenEndpoints();
app.MapLightFxEndpoints();
app.MapImageEndpoints();
app.MapSetupEndpoints();
app.MapLogEndpoints();
app.MapDiagnosticsEndpoints();

// The in-process MCP server (streamable HTTP) — point Claude Code / Claude Desktop at this.
app.MapMcp("/mcp");

// Everything that isn't an API route is the Blazor control panel.
app.MapFallbackToFile("index.html");

app.Logger.LogInformation("RPG Scene Maker started.");
app.Run();

// Exposed so the test project's WebApplicationFactory<Program> can boot the real app.
public partial class Program { }
