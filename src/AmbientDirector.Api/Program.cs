using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using AmbientDirector.Api;
using AmbientDirector.Api.Data;
using AmbientDirector.Api.Endpoints;
using AmbientDirector.Api.Errors;
using AmbientDirector.Api.Logging;
using AmbientDirector.Api.Services;
using AmbientDirector.Api.Services.Audio;
using AmbientDirector.Api.Services.Images;
using AmbientDirector.Api.Services.Sharing;

// The installable Windows build (issue #75) is a self-contained single-file exe, which reports an EMPTY
// entry-assembly location; `dotnet run` and the test host report a real .dll path. That distinguishes "the
// double-clickable app" from development/tests without relying on the environment name.
// IL3000 warns that Location is empty in a single-file app — that empty value is exactly the signal here.
#pragma warning disable IL3000
var isPublishedExe = string.IsNullOrEmpty(System.Reflection.Assembly.GetEntryAssembly()?.Location);
#pragma warning restore IL3000

// The installable build must resolve its content root (the hosted panel's wwwroot and the starter
// scenes.json) from the exe's own folder, so it works no matter which directory it is launched from — a
// double-click sets the cwd to the exe folder, but a Start-menu shortcut, a terminal or an auto-start may
// not. `dotnet run` and tests keep the default content root, so those are unchanged.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = isPublishedExe ? AppContext.BaseDirectory : null,
});

// Each configured storage path is root-resolved once here (Path.GetFullPath), so a RELATIVE operator
// override (e.g. --Images:Path ./imgs) becomes an absolute path against the process CWD and stays consistent
// everywhere downstream — the storage services AND Results.File, which otherwise resolves a non-rooted path
// against the web root and 404s the very file the storage service just wrote under the CWD (issue #90). The
// absolute %LocalAppData% defaults pass through GetFullPath unchanged.

// One-time migration from the pre-rename brand ("RPG Scene Maker"). Older installs kept their data under
// %LocalAppData%\RpgSceneMaker\; on first run of the renamed app, move that whole folder — DB, Spotify
// token, imported sounds/music/images and locale edits — to the new %LocalAppData%\AmbientDirector\ so
// everything carries over automatically. Best-effort: any failure is swallowed so a fresh install still
// starts cleanly (it simply starts empty). Only touches the default location, never a configured override.
try
{
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var legacyRoot = Path.Combine(localAppData, "RpgSceneMaker");
    var currentRoot = Path.Combine(localAppData, "AmbientDirector");
    if (Directory.Exists(legacyRoot) && !Directory.Exists(currentRoot))
    {
        Directory.Move(legacyRoot, currentRoot);
        // The SQLite file was named after the old brand; rename it (and any WAL/SHM sidecars) to match.
        foreach (var (from, to) in new[]
        {
            ("rpg-scene-maker.db", "ambient-director.db"),
            ("rpg-scene-maker.db-wal", "ambient-director.db-wal"),
            ("rpg-scene-maker.db-shm", "ambient-director.db-shm"),
        })
        {
            var src = Path.Combine(currentRoot, from);
            var dst = Path.Combine(currentRoot, to);
            if (File.Exists(src) && !File.Exists(dst)) File.Move(src, dst);
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[startup] Legacy RpgSceneMaker data migration skipped: {ex.Message}");
}

// Scenes and lighting settings live in SQLite; Database:Path overrides the default location.
var dbPath = Path.GetFullPath(builder.Configuration["Database:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AmbientDirector", "ambient-director.db"));
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

// Sound-effect audio files live next to the database; Sounds:Path overrides the location.
var soundsPath = Path.GetFullPath(builder.Configuration["Sounds:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AmbientDirector", "sounds"));

// Full-art tile background images live next to the database; Images:Path overrides the location.
var imagesPath = Path.GetFullPath(builder.Configuration["Images:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AmbientDirector", "images"));

// Local music-library audio files live next to the database; Music:Path overrides the location.
var musicPath = Path.GetFullPath(builder.Configuration["Music:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AmbientDirector", "music"));

// UI translation files live next to the database; Locales:Path overrides the location. Community/agent
// authors drop or edit a <code>.json here; English is also embedded as the fallback (see LocaleService).
var localesPath = Path.GetFullPath(builder.Configuration["Locales:Path"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AmbientDirector", "locales"));

// Startup-captured facts surfaced by GET /diagnostics (developer mode): process start time plus the
// resolved on-disk paths, so the endpoint reuses the exact values instead of re-resolving them.
builder.Services.AddSingleton(new DiagnosticsInfo(DateTimeOffset.UtcNow, dbPath, soundsPath));

// Backs GET /setup/backup — a one-tap download of a consistent SQLite snapshot (issue #110).
builder.Services.AddSingleton(new DbBackupService(dbPath));

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

// Audio output sink (issue #82): the soundboard + local-music players keep their managed NAudio mixing graph
// and only swap the device through IWavePlayerFactory. Pick the backend from Audio:Backend — "auto" (default)
// is NAudio's Windows-only WaveOutEvent on Windows and the cross-platform OpenAL sink elsewhere; "waveout" /
// "openal" force one on any OS (openal-on-Windows is how the cross-platform path is smoke-tested). The OpenAL
// factory owns a process-shared device/context, opened lazily on the first sound played.
var audioBackend = builder.Configuration["Audio:Backend"]?.Trim().ToLowerInvariant();
var useOpenAl = audioBackend switch
{
    "openal" => true,
    "waveout" => false,
    _ => !OperatingSystem.IsWindows(),
};
if (useOpenAl)
    builder.Services.AddSingleton<IWavePlayerFactory, OpenAlPlayerFactory>();
else
    builder.Services.AddSingleton<IWavePlayerFactory, WaveOutPlayerFactory>();

// Soundboard: metadata in SQLite, audio files on disk, playback on the server's own audio device. Now
// cross-platform (the sink comes from the factory above), so the single implementation is registered on every OS.
builder.Services.AddSingleton<SoundStore>();
builder.Services.AddSingleton(new SoundFileStorage(soundsPath));
builder.Services.AddSingleton<ISoundboardPlayer, SoundboardPlayer>();
// Shared import tail (unique id, save, measure, validate, upsert) for /sounds/import + /sounds/library/import.
builder.Services.AddSingleton<SoundImporter>();

// Local music library: track/playlist metadata in SQLite, audio files on disk, playback on the server's own
// audio device (its own output, independent of the soundboard mixer).
builder.Services.AddSingleton<MusicTrackStore>();
builder.Services.AddSingleton<MusicPlaylistStore>();
builder.Services.AddSingleton(new MusicFileStorage(musicPath));
builder.Services.AddSingleton<MusicImporter>();
builder.Services.AddSingleton<AmbientDirector.Api.Services.Music.LocalMusicPlayer>();

// Pluggable music sources behind the IMusicSource seam (mirrors ILightService's Tuya/Hue). The router picks
// one per request; the active-source memory is ephemeral like CurrentState. Sources are scoped (SpotifyClient
// is a scoped typed-HttpClient), so the router that composes them is scoped too.
builder.Services.AddSingleton<AmbientDirector.Api.Services.Music.MusicSourceState>();
builder.Services.AddScoped<AmbientDirector.Api.Services.Music.IMusicSource, AmbientDirector.Api.Services.Music.SpotifyMusicSource>();
builder.Services.AddScoped<AmbientDirector.Api.Services.Music.IMusicSource, AmbientDirector.Api.Services.Music.LocalMusicSource>();
builder.Services.AddScoped<AmbientDirector.Api.Services.Music.MusicRouter>();

// Full-art tile backgrounds: uploaded via /images, stored on disk, referenced by stored file name.
builder.Services.AddSingleton(new ImageFileStorage(imagesPath));
// PDF page → image import (issue #88): renders a picked PDF page into an ordinary stored image. Temp PDFs
// live under <images>/.pdf-tmp and are discarded after import; PDFium's per-instance lock lives in the service.
builder.Services.AddSingleton(sp => new PdfImporter(imagesPath, sp.GetRequiredService<ImageFileStorage>()));

builder.Services.AddSingleton<EventStore>();
builder.Services.AddSingleton<ScreenStore>();
builder.Services.AddSingleton<BoardStore>();
builder.Services.AddSingleton<PartyStore>();
builder.Services.AddSingleton<EncounterStore>();
builder.Services.AddSingleton<LightFxStore>();

// Pluggable RPG game systems (issue #127; docs/GAME-SYSTEMS.md): data-only IGameSystem singletons discovered
// through the registry (the IEnumerable<IImageSearchSource> idiom). Community systems are added by PR — one
// class + its locale keys + one registration line here.
builder.Services.AddSingleton<AmbientDirector.Api.Services.Systems.IGameSystem,
    AmbientDirector.Api.Services.Systems.DaggerheartSystem>();
builder.Services.AddSingleton<AmbientDirector.Api.Services.Systems.IGameSystem,
    AmbientDirector.Api.Services.Systems.Dnd5eSystem>();
builder.Services.AddSingleton<AmbientDirector.Api.Services.Systems.GameSystemRegistry>();

// Shareable content packs (issue #111): a per-kind share descriptor for every content type, a registry over
// them, and the export/import services. Export zips a root entity + its dependency closure + media; import is
// two-phase (inspect → commit) with a light-key remap step. All singletons (every dependency is a singleton).
builder.Services.AddSingleton<IShareDescriptor, SceneShareDescriptor>();
builder.Services.AddSingleton<IShareDescriptor, EventShareDescriptor>();
builder.Services.AddSingleton<IShareDescriptor, LightFxShareDescriptor>();
builder.Services.AddSingleton<IShareDescriptor, SoundShareDescriptor>();
builder.Services.AddSingleton<IShareDescriptor, ScreenShareDescriptor>();
builder.Services.AddSingleton<IShareDescriptor, BoardShareDescriptor>();
builder.Services.AddSingleton<IShareDescriptor, PartyMemberShareDescriptor>();
builder.Services.AddSingleton<IShareDescriptor, EnemyShareDescriptor>();
builder.Services.AddSingleton<IShareDescriptor, EncounterShareDescriptor>();
builder.Services.AddSingleton<ShareRegistry>();
builder.Services.AddSingleton<ShareExporter>();
builder.Services.AddSingleton(sp => new ShareImporter(imagesPath,
    sp.GetRequiredService<ShareRegistry>(),
    sp.GetRequiredService<ImageFileStorage>(),
    sp.GetRequiredService<SoundFileStorage>(),
    sp.GetRequiredService<LocaleService>()));

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
builder.Services.AddSingleton<AmbientDirector.Api.Services.Ai.AiToolService>();

// The in-panel assistant: the tool executor (provider-neutral tool schemas over the façade), the pluggable
// backends (one IAssistantProvider each for Anthropic / OpenAI / Gemini), and the singleton service that
// runs the agentic loop, selects the configured provider per run, and holds the polled transcript.
builder.Services.AddSingleton<AmbientDirector.Api.Services.Ai.AssistantTools>();
builder.Services.AddSingleton<AmbientDirector.Api.Services.Ai.Providers.IAssistantProvider,
    AmbientDirector.Api.Services.Ai.Providers.AnthropicProvider>();
builder.Services.AddSingleton<AmbientDirector.Api.Services.Ai.Providers.IAssistantProvider,
    AmbientDirector.Api.Services.Ai.Providers.OpenAiProvider>();
builder.Services.AddSingleton<AmbientDirector.Api.Services.Ai.Providers.IAssistantProvider,
    AmbientDirector.Api.Services.Ai.Providers.GeminiProvider>();
builder.Services.AddSingleton<AmbientDirector.Api.Services.Ai.AssistantService>();

// MCP server hosted in-process at /mcp (streamable HTTP, stateless — MCP clients resend the API key on every
// request, so there is no session to keep). The tool-type classes are thin adapters over AiToolService.
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<AmbientDirector.Api.Services.Ai.SceneMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.EventMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.ScreenMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.LightFxMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.MusicMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.SoundMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.LibraryMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.PartyMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.EncounterMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.BoardMcpTools>()
    .WithTools<AmbientDirector.Api.Services.Ai.TvMcpTools>();

// In-memory log buffer surfaced by the panel's Logs tab. Whitelist our own logs at Information and
// default everything else (EF SQL, HttpClient request chatter, hosting) to Warning+, so the tab stays
// signal rather than framework noise.
builder.Services.AddSingleton<InMemoryLogStore>();
builder.Services.AddSingleton<ILoggerProvider, InMemoryLoggerProvider>();
builder.Logging.AddFilter<InMemoryLoggerProvider>(null, LogLevel.Warning);
builder.Logging.AddFilter<InMemoryLoggerProvider>("AmbientDirector", LogLevel.Information);

// HttpClient's default request/response logging emits the full request URI at Information — and the Hue
// AppKey travels in that URI path (http://{bridge}/api/{appKey}/…). Raise the transport categories to
// Warning+ for ALL log providers (the in-memory buffer already drops them, but the console — the packaged
// app's UI — would otherwise print the key on every light command, and so would any redirected log file).
// Genuine transport warnings/errors still surface.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// The configured provider picks which system scenes and /lights control ("tuya" or "hue").
builder.Services.AddScoped<ILightService>(sp =>
    sp.GetRequiredService<SettingsStore>().Current.Provider
        .Equals("hue", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<HueLightService>()
        : sp.GetRequiredService<TuyaLightService>());

var app = builder.Build();

// Create/upgrade the database, then pull in data from the legacy JSON files on first run.
{
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!); // dbPath is already absolute (root-resolved above)
    using var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    db.Database.Migrate();
    LegacyImporter.Run(db, app.Configuration, app.Environment, app.Logger);
    // Pre-#127 installs with game data get "daggerheart" stamped + counter keys backfilled, exactly once.
    GameSystemUpgrade.Run(db, app.Logger);
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
         path.StartsWithSegments("/boards") || path.StartsWithSegments("/party") ||
         path.StartsWithSegments("/encounters") || path.StartsWithSegments("/systems") ||
         path.StartsWithSegments("/lightfx") || path.StartsWithSegments("/images") ||
         path.StartsWithSegments("/setup") || path.StartsWithSegments("/logs") ||
         path.StartsWithSegments("/diagnostics") || path.StartsWithSegments("/mcp") ||
         path.StartsWithSegments("/assistant") || path.StartsWithSegments("/i18n") ||
         path.StartsWithSegments("/share") ||
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
app.MapBoardEndpoints();
app.MapPartyEndpoints();
app.MapEncounterEndpoints();
app.MapSystemEndpoints();
app.MapLightFxEndpoints();
app.MapImageEndpoints();
app.MapSetupEndpoints();
app.MapLogEndpoints();
app.MapDiagnosticsEndpoints();
app.MapAssistantEndpoints();
app.MapLocaleEndpoints();
app.MapTvEndpoints();
app.MapShareEndpoints();

// The in-process MCP server (streamable HTTP) — point Claude Code / Claude Desktop at this.
app.MapMcp("/mcp");

// Everything that isn't an API route is the Blazor control panel.
app.MapFallbackToFile("index.html");

app.Logger.LogInformation("Ambient Director started.");

// Once Kestrel is listening, print a friendly banner (the console IS the UI for a double-clicked build)
// and — for the installable Windows build — open the panel in the default browser so the app "just works".
app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
    var (localUrl, lanUrl) = StartupInfo.PanelUrls(addresses, app.Configuration["Urls"]);

    app.Logger.LogInformation(
        "Ambient Director is running.\n" +
        "  On this PC:      {LocalUrl}\n" +
        "  On your network: {LanUrl}\n" +
        "  Close this window to stop the server.",
        localUrl, lanUrl ?? "(no LAN address found — localhost only)");

    // Auto-open the panel in the default browser for the double-click experience. Default ON only for the
    // installable single-file build, OFF under `dotnet run` and tests so they never pop a tab; an explicit
    // Launch:OpenBrowser (appsettings or Launch__OpenBrowser env var / --Launch:OpenBrowser) overrides either way.
    var openBrowser = app.Configuration.GetValue<bool?>("Launch:OpenBrowser") ?? isPublishedExe;
    if (openBrowser)
    {
        app.Logger.LogInformation("Launch:OpenBrowser is on — opening {Url} in your default browser.", localUrl);
        try
        {
            Process.Start(new ProcessStartInfo(localUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning("Could not open a browser automatically ({Message}). Open {Url} yourself.",
                ex.Message, localUrl);
        }
    }
    else
    {
        app.Logger.LogInformation("Launch:OpenBrowser is off — open {Url} in your browser to use the panel.", localUrl);
    }
});

app.Run();

// Exposed so the test project's WebApplicationFactory<Program> can boot the real app.
public partial class Program { }
