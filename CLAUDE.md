# RPG Scene Maker

Local **.NET 10 Minimal API + Blazor WASM touch panel** that switches an RPG table's whole mood
(lights + music) with one tap, from a Stream Deck, iPad, or any browser. Lights are controlled
**locally over the LAN**; music runs through the Spotify Web API (the only cloud dependency).

See [README.md](README.md) for the user-facing setup walkthrough (Tuya, Hue, Spotify, Stream Deck, iPad).

## Projects

Two projects under `src/`; the solution file lives at
[src/RpgSceneMaker.Api/RpgSceneMaker.slnx](src/RpgSceneMaker.Api/RpgSceneMaker.slnx).

- **RpgSceneMaker.Api** — Minimal API (all routes in [Program.cs](src/RpgSceneMaker.Api/Program.cs)).
  It also **hosts** the Blazor WASM panel: it project-references the UI, serves it via
  `UseBlazorFrameworkFiles()`, and falls back non-API routes to `index.html`. So the panel's API base
  address is the same origin.
- **RpgSceneMaker.Ui** — Blazor WASM control panel. Pages in `Pages/` (Scenes, Music, Lights,
  Settings). All server calls go through [ApiClient.cs](src/RpgSceneMaker.Ui/Services/ApiClient.cs).

## Build & run

```powershell
dotnet build src/RpgSceneMaker.Api/RpgSceneMaker.Api.csproj   # builds both (API references UI)
dotnet run   --project src/RpgSceneMaker.Api                  # serves API + panel on http://localhost:5252
```

Running the API is enough to see the panel — it builds and serves the WASM assets.

## Architecture (API)

- **`ILightService`** ([ILightService.cs](src/RpgSceneMaker.Api/Services/ILightService.cs)) — light
  abstraction with two implementations: `TuyaLightService` (local TCP via TuyaNet) and
  `HueLightService` (Hue Bridge REST). The provider is chosen per-request in
  [Program.cs](src/RpgSceneMaker.Api/Program.cs) from `SettingsStore.Current.Provider`. The
  `ApplyAsync(LightSettings)` default-interface method maps a scene's light block to power/colour/white.
- **`SpotifyClient` / `SpotifyStore`** — music is Spotify-only. `SpotifyClient` wraps the Spotify Web
  API (Authorization Code + PKCE, no client secret) to drive a Spotify Connect device on the LAN;
  `SpotifyStore` persists the Client ID, refresh token and preferred device (in SQLite). The OAuth
  connect flow lives under `/setup/spotify/*` in [Program.cs](src/RpgSceneMaker.Api/Program.cs), and
  `/music/*` maps straight onto `SpotifyClient` (play/pause/resume/next/previous/volume/shuffle/repeat +
  playlists/search/state). Playing requires a `spotify:` URI or `open.spotify.com` link.
- **`SceneActivator`** — applies a scene's light/music **concurrently**; each part reports
  ok/skipped/error independently (activation returns HTTP 207 if any part failed).
- **`CurrentState`** — singleton remembering the last activated scene so the panel can highlight it.
- **`SceneStore` / `SettingsStore`** — persistence (see below).

### Conventions

- **Every command endpoint accepts both GET and POST** (`getOrPost`) so the Stream Deck *System →
  Website* action works without a plugin. Keep this when adding command routes.
- **Errors → status codes**: integration failures are thrown as typed exceptions
  (`SpotifyException`, `HueException`, `ArgumentException`, socket/timeout, etc.) and mapped to HTTP
  status + Problem responses by the first middleware in [Program.cs](src/RpgSceneMaker.Api/Program.cs).
  When adding a new failure mode, throw a meaningful exception and add a `switch` arm there rather than
  returning ad-hoc error bodies.
- **Optional API key**: when `Security:ApiKey` is set, `/scenes /lights /music /setup` require it
  (`X-Api-Key` header or `?apiKey=`). The panel stores it in browser localStorage.
- **DTOs are duplicated**, not shared: the UI has its own copies of the request/response shapes in
  [ApiClient.cs](src/RpgSceneMaker.Ui/Services/ApiClient.cs) (there is no shared contracts project).
  If you change an API DTO, update the matching UI DTO by hand.

## Persistence

Scenes and lighting settings live in **SQLite via EF Core**, not appsettings.json. The DB is at
`%LocalAppData%\RpgSceneMaker\rpg-scene-maker.db` (override with `Database:Path`). Context:
[AppDbContext.cs](src/RpgSceneMaker.Api/Data/AppDbContext.cs). Two tables: `Scenes` (Light/Music stored
as JSON columns; ids use `NOCASE` collation) and a single-row `LightingConfig`. The Spotify connection
(Client ID, refresh token, preferred device) is also persisted here via `SpotifyStore`.

Note: `Scene.SoundEffects` is **retained in the schema but currently unused** — the Kenku FM
integration (music + soundboard) was removed and music is now Spotify-only. Existing data is preserved
and the editor passes it through untouched, so soundboard support can return without a migration.

`appsettings.json` holds deployment config only: `Urls`, `Security:ApiKey`, `Database:Path`.

### Changing the schema — create a migration

Any change to `AppDbContext`, the entities
([LightingConfig.cs](src/RpgSceneMaker.Api/Data/LightingConfig.cs),
[Scene.cs](src/RpgSceneMaker.Api/Models/Scene.cs)), or their `OnModelCreating` mapping **requires a new
migration** — the app only applies migrations, it never auto-creates tables from the model. After
editing the model, run from the API project:

```
dotnet dotnet-ef migrations add <Name> -o Data/Migrations
```

Commit the generated files under `Data/Migrations/`. Startup runs `Database.Migrate()` automatically,
so no manual `database update`. `dotnet-ef` is a local tool (restored via `dotnet-tools.json`). Do not
hand-edit a committed migration; add a new one instead.

### Legacy import

On first run with an empty DB, [LegacyImporter.cs](src/RpgSceneMaker.Api/Data/LegacyImporter.cs) imports
the old `scenes.json` and `settings.local.json` once, then never reads them again. `scenes.json` also
serves as the starter scene template on a fresh clone.
