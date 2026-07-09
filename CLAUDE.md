# RPG Scene Maker

Local **.NET 10 Minimal API + Blazor WASM touch panel** that switches an RPG table's whole mood
(lights + music) with one tap, from a Stream Deck, iPad, or any browser. Lights are controlled
**locally over the LAN**; music runs through the Spotify Web API (the only cloud dependency).

See [README.md](README.md) for the user-facing setup walkthrough (Tuya, Hue, Spotify, Stream Deck, iPad).

## Projects

Two projects under `src/`; the solution file lives at
[src/RpgSceneMaker.Api/RpgSceneMaker.slnx](src/RpgSceneMaker.Api/RpgSceneMaker.slnx).

- **RpgSceneMaker.Api** — Minimal API. [Program.cs](src/RpgSceneMaker.Api/Program.cs) wires up DI +
  middleware and calls the endpoint groups in `Endpoints/` (`Scene`/`Light`/`Music`/`Sound`/`Setup`Endpoints,
  one `Map…Endpoints()` extension method each); wire DTOs live in `Contracts/`, request guards in `Validation/`.
  It also **hosts** the Blazor WASM panel: it project-references the UI, serves it via
  `UseBlazorFrameworkFiles()`, and falls back non-API routes to `index.html`. So the panel's API base
  address is the same origin.
- **RpgSceneMaker.Ui** — Blazor WASM control panel. Pages in `Pages/` (Scenes, Music, Lights, Sounds, Settings, Logs);
  reusable components in `Components/`; wire DTOs and editor form models in `Contracts/`; shared
  constants/helpers in `Shared/` (Palette, SceneNaming, LightFormat, UiExtensions). All server calls go
  through [ApiClient.cs](src/RpgSceneMaker.Ui/Services/ApiClient.cs).

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
  connect flow lives under `/setup/spotify/*` in [SetupEndpoints.cs](src/RpgSceneMaker.Api/Endpoints/SetupEndpoints.cs),
  and `/music/*` ([MusicEndpoints.cs](src/RpgSceneMaker.Api/Endpoints/MusicEndpoints.cs)) maps straight onto `SpotifyClient` (play/pause/resume/next/previous/volume/shuffle/repeat +
  playlists/search/state). Playing requires a `spotify:` URI or `open.spotify.com` link.
- **`SceneActivator`** — applies a scene's light/music/sound effects **concurrently**; each part reports
  ok/skipped/error independently (activation returns HTTP 207 if any part failed).
- **`SoundboardPlayer` / `SoundStore` / `SoundFileStorage`** — the soundboard. `SoundboardPlayer` plays
  sound effects on the **server's own audio device** via NAudio (a `MixingSampleProvider` mixes any number
  of overlapping "voices", each with its own volume and optional looping) — this is what Kenku FM used to
  do. `SoundStore` persists per-sound metadata (name/category/volume/loop) in SQLite; `SoundFileStorage`
  keeps the audio files on disk. `/sounds/*` ([SoundEndpoints.cs](src/RpgSceneMaker.Api/Endpoints/SoundEndpoints.cs))
  covers `list`, `import` (multipart), update, delete, play/stop/stop-all, and `state` (playing ids); a scene
  fires its `SoundEffects` (sound ids) on activation. Nothing is mapped at the bare `/sounds` path so the
  panel's Sounds tab can live there (same reason `/lights` uses `/lights/list`). **NAudio output is Windows-only.**
- **`CurrentState`** — singleton remembering the last activated scene so the panel can highlight it.
- **`InMemoryLogStore`** ([InMemoryLogStore.cs](src/RpgSceneMaker.Api/Logging/InMemoryLogStore.cs)) —
  bounded ring buffer of recent log entries, fed by `InMemoryLoggerProvider` (our logs at Information,
  everything else at Warning+) and surfaced by `/logs/list` + the panel's Logs tab. The error middleware
  logs every caught failure here.
- **`SceneStore` / `SettingsStore`** — persistence (see below).

### Conventions

- **Every command endpoint accepts both GET and POST** (`EndpointHelpers.GetOrPost`) so the Stream Deck
  *System → Website* action works without a plugin. Keep this when adding command routes.
- **Errors → status codes**: integration failures are thrown as typed exceptions
  (`SpotifyException`, `HueException`, `ArgumentException`, socket/timeout, etc.) and mapped to HTTP
  status + Problem responses by the first middleware in [Program.cs](src/RpgSceneMaker.Api/Program.cs).
  When adding a new failure mode, throw a meaningful exception and add a `switch` arm there rather than
  returning ad-hoc error bodies.
- **Optional API key**: when `Security:ApiKey` is set, `/scenes /lights /music /sounds /setup /logs` require it
  (`X-Api-Key` header or `?apiKey=`). The panel stores it in browser localStorage.
- **DTOs are duplicated**, not shared: the API's wire DTOs live in `Contracts/` and the UI keeps its own
  copies in [its own `Contracts/`](src/RpgSceneMaker.Ui/Contracts) (there is no shared contracts project).
  If you change an API DTO, update the matching UI DTO by hand.

## Persistence

Scenes and lighting settings live in **SQLite via EF Core**, not appsettings.json. The DB is at
`%LocalAppData%\RpgSceneMaker\rpg-scene-maker.db` (override with `Database:Path`). Context:
[AppDbContext.cs](src/RpgSceneMaker.Api/Data/AppDbContext.cs). Tables: `Scenes` (Light/Music stored
as JSON columns; ids use `NOCASE` collation), `Sounds` (soundboard metadata; ids `NOCASE`) and a
single-row `LightingConfig`. The Spotify connection (Client ID, refresh token, preferred device) is
also persisted here via `SpotifyStore`.

Sound-effect **audio files** live on disk, not in the DB, at `%LocalAppData%\RpgSceneMaker\sounds\`
(override with `Sounds:Path`); each `Sound` row references its file by name via `SoundFileStorage`.
`Scene.SoundEffects` (a `List<string>` JSON column) holds the ids of sounds a scene fires on activation.

`appsettings.json` holds deployment config only: `Urls`, `Security:ApiKey`, `Database:Path`, `Sounds:Path`.

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
