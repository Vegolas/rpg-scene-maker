using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using RpgSceneMaker.Api;
using RpgSceneMaker.Api.Data;
using RpgSceneMaker.Api.Endpoints;
using RpgSceneMaker.Api.Errors;
using RpgSceneMaker.Api.Logging;
using RpgSceneMaker.Api.Services;
using RpgSceneMaker.Api.Services.Images;

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

// UI translation files live next to the database; Locales:Path overrides the location. Community/agent
// authors drop or edit a <code>.json here; English is also embedded as the fallback (see LocaleService).
var localesPath = builder.Configuration["Locales:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RpgSceneMaker", "locales");

// Startup-captured facts surfaced by GET /diagnostics (developer mode): process start time plus the
// resolved on-disk paths, so the endpoint reuses the exact values instead of re-resolving them.
builder.Services.AddSingleton(new DiagnosticsInfo(DateTimeOffset.UtcNow, dbPath, soundsPath));

builder.Services.AddSingleton<TuyaLightService>();
builder.Services.AddSingleton<TuyaSetupService>();
builder.Services.AddHttpClient<HueLightService>(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHttpClient<HueSetupService>(client => client.Timeout = TimeSpan.FromSeconds(10));

// Image search + import: Scryfall over its public API + card-art CDN. Typed client (the source sets the
// User-Agent/Accept Scryfall requires in its ctor); searches are memo-cached, so add the shared cache too.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ScryfallImageSource>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddTransient<IImageSearchSource>(sp => sp.GetRequiredService<ScryfallImageSource>());

builder.Services.AddSingleton<SceneStore>();

// Soundboard: metadata in SQLite, audio files on disk, playback on the server's own audio device.
builder.Services.AddSingleton<SoundStore>();
builder.Services.AddSingleton(new SoundFileStorage(soundsPath));
builder.Services.AddSingleton<SoundboardPlayer>();
// Shared import tail (unique id, save, measure, validate, upsert) for /sounds/import + /sounds/library/import.
builder.Services.AddSingleton<SoundImporter>();

// Full-art tile backgrounds: uploaded via /images, stored on disk, referenced by stored file name.
builder.Services.AddSingleton(new ImageFileStorage(imagesPath));

builder.Services.AddSingleton<EventStore>();
builder.Services.AddSingleton<ScreenStore>();
builder.Services.AddSingleton<LightFxStore>();

// UI translations: JSON files on disk (community-editable), with English embedded as the fallback.
builder.Services.AddSingleton(sp => new LocaleService(localesPath, sp.GetRequiredService<ILogger<LocaleService>>()));

builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<CurrentState>();
// Ephemeral state of the player-facing "/tv" display (what the GM has pushed); like CurrentState it survives
// navigation, not a restart.
builder.Services.AddSingleton<TvState>();
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

// Freesound: token-only cloud API to search + import CC-licensed sounds into the soundboard. The base URL
// is configurable (Freesound:BaseUrl, default https://freesound.org) so verification can point at a mock.
builder.Services.AddSingleton<FreesoundStore>();
builder.Services.AddHttpClient<FreesoundClient>(client => client.Timeout = TimeSpan.FromSeconds(20));

// Assistant: bring-your-own-key settings for the in-panel AI assistant (provider + key + model, in SQLite).
builder.Services.AddSingleton<AssistantStore>();
// The single assistant conversation, persisted so a restart keeps it (hydrated lazily by AssistantService).
builder.Services.AddSingleton<AssistantConversationStore>();

// Shared AI tool layer over scenes/events/light FX (+ read-only context and live control), consumed by the
// MCP server and the in-panel assistant (both added in later commits).
builder.Services.AddSingleton<RpgSceneMaker.Api.Services.Ai.AiToolService>();

// The in-panel assistant: the tool executor (provider-neutral tool schemas over the façade), the pluggable
// backends (one IAssistantProvider each for Anthropic / OpenAI / Gemini), and the singleton service that
// runs the agentic loop, selects the configured provider per run, and holds the polled transcript.
builder.Services.AddSingleton<RpgSceneMaker.Api.Services.Ai.AssistantTools>();
builder.Services.AddSingleton<RpgSceneMaker.Api.Services.Ai.Providers.IAssistantProvider,
    RpgSceneMaker.Api.Services.Ai.Providers.AnthropicProvider>();
builder.Services.AddSingleton<RpgSceneMaker.Api.Services.Ai.Providers.IAssistantProvider,
    RpgSceneMaker.Api.Services.Ai.Providers.OpenAiProvider>();
builder.Services.AddSingleton<RpgSceneMaker.Api.Services.Ai.Providers.IAssistantProvider,
    RpgSceneMaker.Api.Services.Ai.Providers.GeminiProvider>();
builder.Services.AddSingleton<RpgSceneMaker.Api.Services.Ai.AssistantService>();

// MCP server hosted in-process at /mcp (streamable HTTP, stateless — MCP clients resend the API key on every
// request, so there is no session to keep). The tool-type classes are thin adapters over AiToolService.
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<RpgSceneMaker.Api.Services.Ai.SceneMcpTools>()
    .WithTools<RpgSceneMaker.Api.Services.Ai.EventMcpTools>()
    .WithTools<RpgSceneMaker.Api.Services.Ai.ScreenMcpTools>()
    .WithTools<RpgSceneMaker.Api.Services.Ai.LightFxMcpTools>()
    .WithTools<RpgSceneMaker.Api.Services.Ai.MusicMcpTools>()
    .WithTools<RpgSceneMaker.Api.Services.Ai.SoundMcpTools>()
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

// Seed the on-disk locales directory with the shipped translations (only the files that are missing,
// so a community edit is never clobbered). English also stays embedded as the ultimate fallback.
app.Services.GetRequiredService<LocaleService>().Seed();

// Map integration/validation failures to useful status codes so a failing Stream Deck button tells you why.
// The title/detail are localized into the panel's language (sent as the X-Ui-Lang header) against the same
// locale files the panel uses, and a stable machine-readable code (+ args) is emitted in the ProblemDetails
// extensions for Stream Deck / MCP / test consumers. Server logs stay English.
var locales = app.Services.GetRequiredService<LocaleService>();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        var (status, titleKey) = ErrorClassifier.Classify(ex);
        var coded = ex as IErrorCode;
        var lang = context.Request.Headers["X-Ui-Lang"].FirstOrDefault();

        // Real faults (bulb/Spotify unreachable, unexpected) are errors with a stack; "not configured"
        // (503) and bad requests (4xx) are expected states — warn, and skip the noisy stack trace. Logs
        // stay English: the exception's own Message plus the title key, never the localized copy.
        var isFault = status is >= 500 and not 503;
        app.Logger.Log(isFault ? LogLevel.Error : LogLevel.Warning, isFault ? ex : null,
            "{Method} {Path} → {Status} {TitleKey}: {Detail}",
            context.Request.Method, context.Request.Path, status, titleKey, ex.Message);

        var extensions = new Dictionary<string, object?> { ["code"] = coded?.Code ?? titleKey };
        if (coded is not null) extensions["args"] = WireArgs(coded.Args);
        await Results.Problem(
            title: locales.Localize(lang, titleKey),
            detail: coded is not null ? locales.Localize(lang, coded.Code, coded.Args) : ex.Message,
            statusCode: status,
            extensions: extensions).ExecuteAsync(context);

        // Flatten error args for the wire: primitives pass through, a CtxRef becomes { code, args }.
        static object?[] WireArgs(IReadOnlyList<object?> args) =>
            [.. args.Select(a => a is CtxRef c ? (object?)new { code = c.Code, args = c.Args } : a)];
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
            var lang = context.Request.Headers["X-Ui-Lang"].FirstOrDefault();
            await Results.Problem(
                title: locales.Localize(lang, "error.title.unauthorized"),
                detail: locales.Localize(lang, "error.unauthorized.detail"),
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = "error.title.unauthorized" }).ExecuteAsync(context);
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
         path.StartsWithSegments("/diagnostics") || path.StartsWithSegments("/mcp") ||
         path.StartsWithSegments("/assistant") || path.StartsWithSegments("/i18n") ||
         // Player-facing display: only the GM push commands are gated. "/tv/show" also covers
         // "/tv/show/recent" (the history list). "/tv" (the SPA page), "/tv/state" and
         // "/tv/content/current" stay OPEN so a shared table screen never needs the admin key — the
         // only key-free data is the single image the GM deliberately pushed (that exposure is the feature).
         path.StartsWithSegments("/tv/show") || path.StartsWithSegments("/tv/clear"));
});

// The Blazor WASM control panel is served from this same process.
app.UseBlazorFrameworkFiles();
// Serve the PWA web app manifest with the right MIME type. `.webmanifest` is in the
// default provider, but map it explicitly so a trimmed runtime can't 404 it
// (StaticFiles refuses unknown extensions), which would silently break install.
var staticFileTypes = new FileExtensionContentTypeProvider();
staticFileTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = staticFileTypes });

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
app.MapAssistantEndpoints();
app.MapLocaleEndpoints();
app.MapTvEndpoints();

// The in-process MCP server (streamable HTTP) — point Claude Code / Claude Desktop at this.
app.MapMcp("/mcp");

// Everything that isn't an API route is the Blazor control panel.
app.MapFallbackToFile("index.html");

app.Logger.LogInformation("RPG Scene Maker started.");
app.Run();

// Exposed so the test project's WebApplicationFactory<Program> can boot the real app.
public partial class Program { }
