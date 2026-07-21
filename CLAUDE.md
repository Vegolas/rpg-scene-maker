# Ambient Director

Local **.NET 10 Minimal API + Blazor WASM touch panel** that switches an RPG table's whole mood
(lights + music) with one tap, from a Stream Deck, iPad, or any browser. Lights are controlled
**locally over the LAN**; music runs through the Spotify Web API (the only cloud dependency).

See [README.md](README.md) for the user-facing setup walkthrough (Tuya, Hue, Spotify, Stream Deck, iPad).

## Task tracking

The backlog lives in **GitHub Issues** ([Vegolas/ambient-director/issues](https://github.com/Vegolas/ambient-director/issues)),
not in `roadmap.md`. Issues are the source of truth for what to work on.

- **Labels**: every issue gets an area label (`area: ui`, `area: api`, `area: lights`, `area: music`,
  `area: sound`, `area: infra`) plus a type (`enhancement` / `bug`). Add a new `area:` label rather than
  overloading an existing one.
- **Branches & PRs**: work on a `claude/<slug>` branch and open a PR that closes the issue
  (`Closes #N` in the body). CI ([.github/workflows/build.yml](.github/workflows/build.yml)) runs
  `dotnet build` on every PR — keep it green.
- **`gh` is authenticated** in this environment, so read/create/update issues directly
  (`gh issue list`, `gh issue create`, `gh issue develop`, …) instead of editing a file.

## Projects

Two projects under `src/`; the solution file lives at
[src/AmbientDirector.Api/AmbientDirector.slnx](src/AmbientDirector.Api/AmbientDirector.slnx).

- **AmbientDirector.Api** — Minimal API. [Program.cs](src/AmbientDirector.Api/Program.cs) wires up DI +
  middleware and calls the endpoint groups in `Endpoints/` (`Scene`/`Light`/`Music`/`Sound`/`Event`/`Screen`/`Setup`Endpoints,
  one `Map…Endpoints()` extension method each); wire DTOs live in `Contracts/`, request guards in `Validation/`.
  It also **hosts** the Blazor WASM panel: it project-references the UI, serves it via
  `UseBlazorFrameworkFiles()`, and falls back non-API routes to `index.html`. So the panel's API base
  address is the same origin.
- **AmbientDirector.Ui** — Blazor WASM control panel. Pages in `Pages/` (Scenes, Screens, Music, Lights, Sounds, Events, Effects, Boards, Party, TV, Assistant, Settings, Logs — the Assistant tab is the BYOK chat, polling `/assistant/state`, and only appears in the nav once a provider key is configured; the **TV** page is the key-free player-facing display, see `TvState` below; **Boards** (pl "Tablice") is the composable-TV-content editor, see `BoardStore`; **Party** (pl "Drużyna") is the touch ± roster tracker (`/party`) + per-player editor (`/party/{id}`), see `PartyStore`; the first-run **onboarding wizard** is an overlay, [`OnboardingWizard.razor`](src/AmbientDirector.Ui/Components/OnboardingWizard.razor), not a nav tab);
  reusable components in `Components/`; wire DTOs and editor form models in `Contracts/`; shared
  constants/helpers in `Shared/` (Palette, SceneNaming, LightFormat, UiExtensions, Icons). All server calls go
  through [ApiClient.cs](src/AmbientDirector.Ui/Services/ApiClient.cs). **UI text is localized at runtime** by the
  injected `Localizer` ([Localizer.cs](src/AmbientDirector.Ui/Services/Localizer.cs)): components read strings by
  dotted key (`@L["nav.scenes"]`, `L.Format`, `L.Plural`), the language is a per-device localStorage pref,
  `App.razor` loads strings from `/i18n` before first render, and switching language (Settings → Language)
  swaps the active table + re-renders with no reload. `Palette`/`LightFormat` label helpers return i18n keys
  (not text). **UI chrome icons** are Phosphor Fill
  SVGs: [`Icon.razor`](src/AmbientDirector.Ui/Components/Icon.razor) renders a glyph by semantic name from
  [`Icons.cs`](src/AmbientDirector.Ui/Shared/Icons.cs) (tinted `currentColor`); user-picked scene/sound emoji
  stay content, and [`Glyph.razor`](src/AmbientDirector.Ui/Components/Glyph.razor) shows a user's emoji or falls
  back to a chrome icon. The top bar (in
  [MainLayout.razor](src/AmbientDirector.Ui/Layout/MainLayout.razor)) hosts always-visible
  [QuickControls.razor](src/AmbientDirector.Ui/Components/QuickControls.razor): music play/pause + volume
  (shown only when Spotify is connected) and a reset-lights button, on every tab.
  **Styling is SCSS**: sources in [Styles/](src/AmbientDirector.Ui/Styles) (partials per concern, tokens in
  `_tokens.scss`), compiled to `wwwroot/css/app.css` by `AspNetCore.SassCompiler` during `dotnet build` —
  the generated CSS is gitignored, never edit it. The visual language is the **"Control Room" design
  system**: tokens + rules live in [docs/design/](docs/design) (STYLE-GUIDE.md, tokens.css); follow it for
  any UI work (solid dark surfaces, one blue accent, ≥44px targets, scene colors only for user content).

## Build & run

```powershell
dotnet build src/AmbientDirector.Api/AmbientDirector.Api.csproj   # builds both (API references UI)
dotnet run   --project src/AmbientDirector.Api                  # serves API + panel on http://localhost:5252
```

Running the API is enough to see the panel — it builds and serves the WASM assets.

## Architecture (API)

- **`ILightService`** ([ILightService.cs](src/AmbientDirector.Api/Services/ILightService.cs)) — light
  abstraction with two implementations: `TuyaLightService` (local TCP via TuyaNet) and
  `HueLightService` (Hue Bridge REST). The provider is chosen per-request in
  [Program.cs](src/AmbientDirector.Api/Program.cs) from `SettingsStore.Current.Provider`. The
  `ApplyAsync(LightSettings)` default-interface method maps a scene's light block to power/colour/white.
  A configurable **default state** (`LightingConfig.DefaultLight`) is applied by `GET/POST /lights/default`
  — the panel's always-visible "reset lights" button; set it on the Settings page (400s until then).
- **`SpotifyClient` / `SpotifyStore`** — Spotify is *one* music source, behind the `IMusicSource` seam
  (below). `SpotifyClient` wraps the Spotify Web
  API (Authorization Code + PKCE, no client secret) to drive a Spotify Connect device on the LAN;
  `SpotifyStore` persists the Client ID, refresh token and preferred device (in SQLite). The OAuth
  connect flow lives under `/setup/spotify/*` in [SetupEndpoints.cs](src/AmbientDirector.Api/Endpoints/SetupEndpoints.cs),
  and the Spotify-specific browser (`/music/playlists`, `/music/search`) still maps straight onto
  `SpotifyClient`; the `/music/*` transport now routes through the `MusicRouter` (below), with
  `SpotifyMusicSource` a thin `IMusicSource` adapter over `SpotifyClient` (left intact).
- **`IMusicSource` / `MusicRouter` / local music library** — music is **pluggable** (mirroring how
  `ILightService` abstracts Tuya/Hue): [`IMusicSource`](src/AmbientDirector.Api/Services/Music/IMusicSource.cs)
  (play/pause/resume/next/previous/volume/shuffle/repeat/state, a neutral `MusicState`) has two
  implementations — `SpotifyMusicSource` and `LocalMusicSource` (over `LocalMusicPlayer`). A scoped
  `MusicRouter` picks the source per request: `/music/play?id=` **infers it from the id shape**
  (`spotify:`/`open.spotify.com` → Spotify; `local:track:{id}` / `local:playlist:{id}` → local, via
  `LocalMusicId`), the bare transport (`pause`/`resume`/`next`/`previous`/`volume`/`shuffle`/`repeat`)
  targets the *active* source (the last successful play, remembered in the ephemeral singleton
  `MusicSourceState`, like `CurrentState`) with an optional `?source=` override, and `/music/state`
  returns the neutral state + the active `source` + the `available` source keys. `MusicSettings.Source`
  gives a scene the same discriminator (null = infer from `PlayId`). **Local library**:
  `MusicTrack`/`MusicPlaylist` (SQLite, NOCASE ids) + `MusicTrackStore` / `MusicPlaylistStore` /
  `MusicFileStorage` / `MusicImporter` mirror the `Sound*` trio (files at
  `%LocalAppData%\AmbientDirector\music\`, override `Music:Path`) and back `/music/library/*` (tracks +
  playlists CRUD and a multipart `import`). [`LocalMusicPlayer`](src/AmbientDirector.Api/Services/Music/LocalMusicPlayer.cs)
  (singleton, NAudio, **cross-platform** like `SoundboardPlayer` — its own output device via the shared
  `IWavePlayerFactory` seam, one stream at a time, *not* the soundboard mixer) plays a queue with shuffle +
  repeat off/track/playlist, advancing at each track's natural end. **All output-device creation is one lazy
  method wrapped in `SoundboardException` → 503**, so a host with no audio device degrades cleanly instead of
  crashing.
  Deleting a track releases its file if it's playing, scrubs the id from every playlist, and nulls any
  scene `Music.PlayId` that pointed at it (keeping volume) — like the sound-delete scrub.
- **`SceneActivator` / `SceneLightApplier`** — `SceneActivator` applies a scene's light/music/sound effects
  **concurrently**; each part reports ok/skipped/error independently (activation returns HTTP 207 if any part
  failed). The light half lives in `SceneLightApplier` (per-light entries + legacy "all lights" block,
  starting/stopping `EffectEngine` loops), extracted so `EventActivator` can reuse it.
- **`EventActivator`** — fires a one-shot **event** (`GameEvent`, [GameEvent.cs](src/AmbientDirector.Api/Models/GameEvent.cs)):
  a brief light **flash** (jump to a colour, hold `DurationMs`, then restore the live scene's lights via
  `SceneLightApplier`, else the configured default light) and/or **sounds** that *overlay* current playback
  (no `StopAll`, unlike a scene). Light + sound run concurrently and each reports ok/skipped/error (207 if any
  failed). `/events/*` ([EventEndpoints.cs](src/AmbientDirector.Api/Endpoints/EventEndpoints.cs)) covers `list`,
  get/put/delete, `trigger`, `stop` and `state`; like `/sounds`, nothing is mapped at the bare `/events` path
  so the panel's Events tab can live there.
- **`EventTimelineRunner`** — plays an event's optional **timeline** (`GameEvent.Timeline`: sound and light
  *clips* placed at ms offsets with durations, edited in the panel's video-editor-style timeline). A non-empty
  timeline makes `trigger` start this singleton runner and return immediately (`"started"`, HTTP 200); the
  legacy flash+sounds path is unchanged. Only one timeline runs at a time (re-trigger restarts, `/events/stop`
  cancels, `/events/state` reports the running id). Each clip is its own delayed task: sound clips get per-voice
  stop handles (`SoundboardPlayer.StopVoice`) so an explicit window cuts them off, natural-end one-shots hold
  the run open, and a windowless *looping* clip holds the run open and plays until the run is stopped; each
  animated light clip runs as its own **job group** on the shared `EffectEngine` (`StartGroupAsync`/`StopGroup`;
  a global `StopAll` — reset-lights, scene activation — stops clip effects too, since that caller is taking
  over the lights). When the timeline touched the lights they are restored afterwards via
  `SceneLightApplier.RestoreLightsAsync` (shared with the flash path). The runner is a singleton but
  `ILightService` is scoped, so each run creates one service scope for its lifetime.
- **`LightFxStore` / `LightFxTester`** — the reusable **Light FX library** (`LightFx`, [LightFx.cs](src/AmbientDirector.Api/Models/LightFx.cs)):
  a named keyframe sequence (same shape as a "custom" `LightEffect`). Scene lights and event-timeline light
  clips reference one by id via a new `LightEffect.FxId` and `Type == "fx"`, resolved to a materialized "custom"
  effect at apply time in `SceneLightApplier` / `EventTimelineRunner` (once per activation, not per engine tick;
  a missing FX warns and degrades to a static light — `EffectEngine` is untouched). `/lightfx/*`
  ([LightFxEndpoints.cs](src/AmbientDirector.Api/Endpoints/LightFxEndpoints.cs)) covers `list`, put/delete and a
  bounded `{id}/test` + `test/stop` (the `LightFxTester` singleton previews an FX as an `EffectEngine` group for
  a window, then restores via `SceneLightApplier.RestoreLightsAsync`). **Deleting an FX detaches it**: every
  referencing scene light / timeline clip is rewritten in place to embed a "custom" copy of the keyframes (like
  the sound-delete scrub), so nothing dangles. Like `/screens`, there is deliberately no `GET /lightfx/{id}` and
  nothing at the bare `/lightfx` path; the panel's Effects pages live at `/effects` and `/effects/{id}` and read
  the library from `/lightfx/list`.
- **`ScreenStore`** — persistence for **screens** (`Screen`, [Screen.cs](src/AmbientDirector.Api/Models/Screen.cs)):
  named boards of *shortcut tiles* (`ScreenTile` = a `Kind` of scene/event/sound/music/light-reset, a `Ref`
  — the entity id, or a Spotify URI for music — and a `Label`) that group existing entities onto one
  tap-friendly screen. Purely organizational: a screen owns no light/music/sound state and has no
  `/trigger`; the panel's tiles just call the existing `/scenes`, `/events`, `/sounds`, `/music` and
  `/lights` endpoints. `/screens/*` ([ScreenEndpoints.cs](src/AmbientDirector.Api/Endpoints/ScreenEndpoints.cs))
  covers `list` and put/delete only — there is deliberately **no** `GET /screens/{id}` (and nothing at the
  bare `/screens`) so full-page loads of the panel's `/screens` and `/screens/{id}` pages fall through to
  `index.html` (the panel reads `/screens/list` and picks a board by id client-side).
- **`BoardStore` / `BoardEndpoints`** — persistence + CRUD for **boards** (`Board`, [Board.cs](src/AmbientDirector.Api/Models/Board.cs)):
  persisted, composable player-facing TV content — a fixed **16:9 stage** described entirely in percent
  coordinates (background solid colour or stored image, plus positioned `image`/`text`/`party` elements —
  a `party` element is a geometry-only placeholder that renders the live roster, see `PartyStore`; **element
  list order is the z-order**, there is deliberately no Z field). Not to be confused with `Screens`
  (panel-side shortcut launchers): a board is *content pushed to* the `/tv` display via
  `/tv/show?board={id}`. `/boards/*` ([BoardEndpoints.cs](src/AmbientDirector.Api/Endpoints/BoardEndpoints.cs))
  follows the `/screens` house pattern exactly — `list`, put and delete only, deliberately **no**
  `GET /boards/{id}` and nothing at the bare `/boards` path (the panel's `/boards` + `/boards/{id}` pages
  fall through to index.html), and `/boards` is in `IsProtectedPath`. Board images follow the entity-art
  ownership pattern generalized to a **file set** (`Board.ReferencedFiles()` = background + image elements):
  upsert diffs the old vs new set and deletes dropped files, delete releases them all. Upserting the
  currently-shown board calls `TvState.TouchBoard` (rev bump → an open TV re-renders within one 2 s poll —
  the codebase's real-time idiom, no SSE); deleting it calls `TvState.ForgetBoard` (clears the display and
  scrubs it from Recent, like the sound-delete scrub). Board AI ops (`list_boards`/`get_board`/`upsert_board`/
  `delete_board`) live on the shared `AiToolService` façade (#89, both AI surfaces). The panel side is the **Boards** tab (pl "Tablice"):
  `/boards` list + `/boards/{id}` editor (numeric controls + a live 16:9 preview; on-canvas drag/resize is a
  follow-up), with [`BoardCanvas.razor`](src/AmbientDirector.Ui/Components/BoardCanvas.razor) as the one
  shared renderer (TV, editor preview, list cards, remote rail — text sizes in `cqh` container units so it
  scales identically everywhere).
- **`PartyStore` / `PartyEndpoints`** — the **party tracker** (#88, Phase 3): live table stats on the TV.
  The **players** domain is `PartyMember` ([PartyMember.cs](src/AmbientDirector.Api/Models/PartyMember.cs);
  named `PartyMember`, not `Player`, to dodge the audio `…Player` collision — the wire/route vocabulary is
  still "players") with a name, a stored `Portrait` (owned like a board image) and **generic** counters
  `PartyCounter` = `{label, value, max, style}` (style null/`pips`/`number`); the Daggerheart HP/Stress/Armor/
  Hope loadout is a **UI-side preset**, never hardcoded here. Table-wide stats that belong to no one player
  (Daggerheart's Fear) live on the single-row **`PartyConfig`** ([PartyConfig.cs](src/AmbientDirector.Api/Data/PartyConfig.cs),
  the `LightingConfig` idiom), reusing the same `PartyCounter`. `/party/*`
  ([PartyEndpoints.cs](src/AmbientDirector.Api/Endpoints/PartyEndpoints.cs)) covers `list` (a `PartyDto` of
  players + table counters), player put/delete, table-counter put, and two GetOrPost **adjust** commands
  (`/party/players/{id}/adjust` and `/party/counters/adjust`, `?counter=<label>&delta=` or `&value=`, exactly
  one — so a Stream Deck button can do `-1 HP`); adjust clamps into `[0, max ?? 999]`. Like `/boards` there is
  deliberately **no** `GET /party/players/{id}` and nothing at bare `/party`, so the panel's `/party` +
  `/party/{id}` pages fall through to index.html, and `/party` is in `IsProtectedPath`. Any player/counter
  change calls `TouchIfPartyShown` — a rev bump **only if** the currently-shown board has a `party` element —
  so an open TV re-fetches within one 2 s poll (no pointless bumps otherwise). The TV gate is extended for
  this: `/tv/content/board/{name}` also serves a **current member's portrait** key-free, but **only while a
  party-element board is shown** (membership checked before any disk access, like the board-files check).
  Party/bestiary AI ops (`list_party`, player + `save_table_counters` + enemy CRUD, and the `adjust_*` commands)
  live on the shared `AiToolService` façade (#89, both AI surfaces), with the same `TouchIfPartyShown` rev-bump.
  **Panel side**: the **Party** tab (pl "Drużyna") is the fast, touch-first mid-session tracker at `/party` —
  table counters up top and a card per player with ± step buttons on every counter (each tap hits `/party/.../adjust`;
  a shown party board updates live) — plus a per-player editor at `/party/{id}` (name, **ArtField** portrait, order,
  the counter set with pips/number style, and the UI-only **Add Daggerheart set** / **Add Fear** presets). The one
  `BoardCanvas` renderer draws a `kind="party"` element everywhere from a live render model: inlined by the API on the
  TV, and built in the panel (editor preview, board list cards, remote rail) from `/party/list` via
  `PartyRender.ToRenderModel` (threaded through `BoardRender.ToRenderModel`).
- **`ISoundboardPlayer` / `SoundboardPlayer` / `IWavePlayerFactory` / `SoundDecoder` / `SoundStore` / `SoundFileStorage`** —
  the soundboard, behind the `ISoundboardPlayer` seam ([ISoundboardPlayer.cs](src/AmbientDirector.Api/Services/ISoundboardPlayer.cs):
  `Play`/`Stop`/`StopVoice`/`StopAll`/`PlayingIds`, mirroring how `ILightService` abstracts Tuya/Hue).
  `SoundboardPlayer` plays sound effects on the **server's own audio device** via NAudio (a
  `MixingSampleProvider` mixes any number of overlapping "voices", each with its own volume and optional
  looping) — this is what Kenku FM used to do. The **whole mixing graph is managed NAudio and cross-platform
  (#82)**; the only platform-specific piece is the output device, taken from the injected
  [`IWavePlayerFactory`](src/AmbientDirector.Api/Services/Audio/IWavePlayerFactory.cs) — `WaveOutPlayerFactory`
  (NAudio `WaveOutEvent`, Windows) or `OpenAlPlayerFactory` (a Silk.NET OpenAL sink over the bundled OpenAL
  Soft, everywhere else). Program.cs picks the backend from **`Audio:Backend`** (`auto` = WaveOut on Windows /
  OpenAL elsewhere; `waveout`/`openal` force one on any OS — `openal`-on-Windows is how the cross-platform path
  is smoke-tested). The OpenAL sink shares one process-wide device/context ([`OpenAlContext`](src/AmbientDirector.Api/Services/Audio/OpenAlContext.cs))
  and each [`OpenAlWavePlayer`](src/AmbientDirector.Api/Services/Audio/OpenAlWavePlayer.cs) runs a background
  pump thread feeding one source from a rotating buffer queue. A host with no output device surfaces the
  localized `SoundboardException` (→ 503 / a 207 on scene activation) at play time, so it degrades cleanly
  rather than crashing. `DiagnosticsDto.SoundboardSupported` is now always true (the panel's old "unavailable
  on this OS" banner no longer shows). File decoding lives in the shared, platform-agnostic
  [`SoundDecoder`](src/AmbientDirector.Api/Services/Audio/SoundDecoder.cs) (`CreateReader`/`Normalize` +
  `TryMeasureDurationMs`/`TryComputeWaveform`, called by type name — never injected): **managed WAV/OGG/MP3
  decode on every OS** (MP3 via NLayer, not the Windows ACM codec — routed to NLayer on all OSes so a Windows
  run exercises the exact Linux path). `SoundStore` persists
  per-sound metadata (name/category/volume/loop, plus `DurationMs` — measured at import, lazily backfilled on
  `list`; the timeline editor uses it to size clips) in SQLite; `SoundFileStorage` keeps the audio files on
  disk. `/sounds/*` ([SoundEndpoints.cs](src/AmbientDirector.Api/Endpoints/SoundEndpoints.cs)) covers `list`,
  `import` (multipart), update, delete, play/stop/stop-all, and `state` (playing ids); a scene fires its
  `SoundEffects` (sound ids) on activation. Deleting a sound also scrubs its id from every scene's and event's
  `SoundEffects` and from event timeline clips, so activations/triggers never warn about a dangling reference.
  Nothing is mapped at the bare `/sounds` path so the panel's Sounds tab can live there (same reason `/lights`
  uses `/lights/list`).
- **`FreesoundStore` / `FreesoundClient` / `SoundImporter`** — the **Freesound** sound-library integration
  (issue #73): search + import CC-licensed effects straight into the soundboard. `FreesoundClient` is a typed
  `HttpClient` (base URL `Freesound:BaseUrl`, default `https://freesound.org`, so verification can point at a
  mock) hitting the token-auth Freesound API; `FreesoundStore` persists the API token (single-row
  `FreesoundConfig`, **never echoed** — the config endpoints return only `{configured}`). `SoundImporter` is the
  shared import tail (unique id, save, measure `DurationMs`, validate, upsert) reused by both `/sounds/import`
  (multipart) and `/sounds/library/import` (fetch a chosen Freesound preview). `/sounds/library/*`
  ([SoundEndpoints.cs](src/AmbientDirector.Api/Endpoints/SoundEndpoints.cs)) adds `search` + `import`; the token is
  managed over `/setup/freesound/*` ([SetupEndpoints.cs](src/AmbientDirector.Api/Endpoints/SetupEndpoints.cs))
  (`config` get/put + `disconnect`). Upstream failures throw **`FreesoundException`** → 502
  (`error.title.freesound`), classified in `ErrorClassifier` like Hue/Spotify.
- **`ImageFileStorage` / image search + import (`Services/Images/`)** — entity tile art lives on disk at
  `%LocalAppData%\AmbientDirector\images\` (override `Images:Path`), one file per image, referenced by stored
  file name only; `ImageFileStorage` mirrors `SoundFileStorage` (traversal-guarded names, 10 MB cap). Besides
  the browser `POST /images/upload` (multipart) and `GET /images/{name}` (byte server), the panel can search
  a provider and import a picked image server-side through the **`IImageSearchSource`** seam
  ([IImageSearchSource.cs](src/AmbientDirector.Api/Services/Images/IImageSearchSource.cs)): a source exposes an
  `Id`/`Name`/English `Attribution`, a `SearchAsync` → `ImageSearchResponseDto`, a `CanFetch(Uri)` host
  allowlist, and a streaming `FetchImageAsync`. The one implementation is `ScryfallImageSource` (typed
  `HttpClient` with the Scryfall-required `User-Agent`/`Accept` set in its ctor) — it hits
  `api.scryfall.com/cards/search?...&unique=art`, maps each card (or each face of a double-faced card) to its
  `art_crop`, caps at 60 hits, treats a 404 as empty results, maps a 400 to `error.imageSearch.badQuery`, and
  memo-caches successful searches for 15 min in `IMemoryCache`. `SearchAsync` takes an **`ImageSearchOptions`**
  (`FullImage`, `IncludeExtras`; the picker's two toggles): `FullImage` maps each hit to the whole card scan
  instead of the tight crop (thumbnail `normal`, imported `large`, falling back down the size chain — the app
  crops it in-app), and `IncludeExtras` appends `include:extras` so tokens/art-series/emblems Scryfall hides
  by default appear. Both are part of the cache key (they change the query or the mapped URLs), and both
  full-card sizes live on the same `cards.scryfall.io` host so import needs no allowlist change. `/images/*`
  ([ImageEndpoints.cs](src/AmbientDirector.Api/Endpoints/ImageEndpoints.cs)) adds `GET /sources` (over the
  injected `IEnumerable<IImageSearchSource>`), `GET /search?source=&q=&full=&extras=`, and `POST /import` ({ url } — fetches
  an allowlisted https URL, re-checks the host after redirects, derives the extension from `Content-Type`
  (URL-ext fallback), enforces the 10 MB cap on both `Content-Length` and the streamed copy, then saves via
  `ImageFileStorage.SaveAsync`). Upstream Scryfall/CDN failures throw **`ImageSourceException`** → 502
  (`error.title.imageSource`), classified in `ErrorClassifier` like Hue/Spotify/AiProvider.
  **PDF page import (`PdfImporter`, issue #88)**: the GM uploads a PDF (`POST /images/pdf/upload`, 25 MB cap),
  the server renders per-page thumbnails (`GET /images/pdf/{id}/thumb/{page}`) and, for the picked page(s),
  full-quality images (`POST /images/pdf/{id}/import`) saved as ordinary `ImageFileStorage` files — so an
  imported page then works everywhere images do (tile art, `/tv/show`). Rendering is PDFium + SkiaSharp via the
  **PDFtoImage** package (all through one lock — PDFium isn't guaranteed thread-safe). Pages are **1-based** on
  the whole surface; no PDF is ever persisted — the upload is a temp under `<images>/.pdf-tmp` (1 h TTL, swept
  on the next upload, deleted after import), guarded by a `^[a-z0-9]{12}$` id check before any path use. No new
  `TvContent` kind and no EF schema change. The panel side is `PdfPagePicker.razor`, opened from `ArtField` and
  the TV remote.
- **`IGameSystem` / `GameSystemRegistry` / `SystemEndpoints`** — the pluggable **RPG game-system contract**
  (#127, design spec: [docs/GAME-SYSTEMS.md](docs/GAME-SYSTEMS.md) — read it before touching this layer;
  phases 2–3 are #128/#129). An [`IGameSystem`](src/AmbientDirector.Api/Services/Systems/IGameSystem.cs) is a
  data-only DI singleton (id, `NameKey` i18n key, member/enemy/table `CounterPreset`s with curated
  `GameSystemGlyphs` names, a `Quickbar` of table-counter keys, a `SpotlightLabel` TV literal) discovered via
  `GameSystemRegistry` (the `IEnumerable<IImageSearchSource>` idiom; unique-slug-id asserted at startup).
  Community systems come in **by PR** (class + locale keys + one Program.cs registration), never plugin DLLs;
  the contract grows only by default interface members / capability marker interfaces. The choice persists in
  `PartyConfig.SystemId` (tri-state: null = never chosen, `"none"` = explicitly none, else an id — the wire
  shows only null-or-id); `GET /systems/list` + GetOrPost `/systems/current?id=` (`/systems` is in
  `IsProtectedPath`, nothing at the bare path) select it, **seeding** the system's table counters
  adopt-or-append (never overwriting values). `GameSystemUpgrade` (Program.cs startup, the LegacyImporter
  idiom) auto-stamps `daggerheart` once when pre-#127 game data exists and backfills semantic counter keys by
  EN/PL label. `PartyCounter.Key` is that stable semantic id ("hp", "fear"): optional, lowercase slug, unique
  per owner, stamped by presets and kept on label rename; every counter **adjust resolves key first, then
  label** (so `?counter=fear` works on a Polish table). Panel side: no active system hides the **Encounters
  nav tab only** (`UiState.GameSystem`, the `AssistantConfigured` idiom — navigation-only, API/TV/deep links
  unaffected), Settings → General has the system dropdown, and `/party/list` (+ both AI surfaces' `list_party`)
  carries the active `system` id. Daggerheart's presets/colors are pinned in the spec's reference table — a
  Daggerheart table must render identically across the refactor phases.
- **`CurrentState`** — singleton remembering the last activated scene so the panel can highlight it.
- **`TvState` / `TvEndpoints`** — the player-facing **`/tv` display** (issue #80): an ephemeral singleton
  (`TvState`, like `CurrentState` — survives navigation, not a restart) holding the single piece of content the
  GM has pushed to a shared table screen — an image/handout or a **board** (`TvContent.Kind` = `"image"` |
  `"board"`, `Ref` = stored file name or board id; see `BoardStore`) — plus a small recent-history list and a
  monotonic `rev` for the panel's `/tv/state?rev=` poll (bumped on show/clear and on a live edit of the shown
  board). `/tv/*` ([TvEndpoints.cs](src/AmbientDirector.Api/Endpoints/TvEndpoints.cs)) covers `state` (for a
  board it inlines the render model, image refs pre-resolved to `/tv/content/board/{name}?rev=`),
  `content/current` (streams the current image's bytes via `Results.File`; 404 once cleared/missing/board),
  `content/board/{name}` (streams one image of the currently-shown board), `show`/`clear` (GET+POST push
  commands; `?image=` or `?board=`, exactly one) and `show/recent`. **Gate split**: only `/tv/show*` +
  `/tv/clear` are in `IsProtectedPath`; `/tv`, `/tv/state` and `/tv/content/*` stay OPEN so a shared player
  screen never needs the admin key — the only key-free data is what the GM deliberately pushed:
  `/tv/content/board/{name}` serves **only file names the currently-shown board references** (plus a shown
  party board's live member portraits — see `PartyStore`; membership checked before any disk access, so it is
  never a file-existence oracle; the general `/images` route stays gated). Nothing is mapped at bare `/tv` (like `/screens`) so the panel's `/tv` page falls through to
  `index.html`.
- **First-run onboarding (#75)** — a guided setup overlay the panel shows on a fresh install. State lives in a
  nullable `OnboardingDoneUtc` column on the single-row `LightingConfig`: `GET /setup/onboarding` returns `show`
  (= the flag is still null) plus per-step "already configured" hints, and **auto-completes for upgrades** (if
  lights/Spotify/local-music are already set up but the flag is null, it stamps done so long-time users never see
  the wizard); `GET|POST /setup/onboarding/done` stamps it via `SettingsStore.MarkOnboardingDone()`. The UI is
  [`OnboardingWizard.razor`](src/AmbientDirector.Ui/Components/OnboardingWizard.razor), reusing the Settings forms;
  every step is skippable, and Settings has a "Run setup wizard" button that reopens the overlay locally without
  clearing the flag.
- **`InMemoryLogStore`** ([InMemoryLogStore.cs](src/AmbientDirector.Api/Logging/InMemoryLogStore.cs)) —
  bounded ring buffer of recent log entries, fed by `InMemoryLoggerProvider` (our logs at Information,
  everything else at Warning+) and surfaced by `/logs/list` + the panel's Logs tab. The error middleware
  logs every caught failure here.
- **`LocaleService`** ([LocaleService.cs](src/AmbientDirector.Api/Services/LocaleService.cs)) — serves the
  panel's **UI translations**. Each language is a JSON file (`<code>.json`, e.g. `en`/`pl`) in the on-disk
  locales dir (`%LocalAppData%\AmbientDirector\locales\`, override `Locales:Path`), read on demand so a
  community/agent edit needs no restart. `en.json`+`pl.json` also ship **embedded** in the assembly: seeded to
  disk on first run (missing files only, never clobbering edits). For these shipped codes `Get` **merges per
  key** — the embedded strings are the base and the on-disk file overlays them (disk wins key-by-key) — so
  community edits are preserved while newly shipped keys always appear even when the on-disk file was seeded
  by an older build (a stale file can't hide a new key), and a missing/broken file still can't blank the UI
  (embedded only). Community languages with no embedded counterpart are served from disk as-is.
  `/i18n/*` ([LocaleEndpoints.cs](src/AmbientDirector.Api/Endpoints/LocaleEndpoints.cs))
  covers `list` and `{code}` (GET only); like `/screens` there is nothing at bare `/i18n`. The panel's
  `Localizer` fetches English + the active language and falls back to English per missing key. **Server-side
  error/validation messages are localized too** (via the `Errors/` layer below), against the same locale
  files under an `error.*` key namespace — keyed by the panel's language, which the client sends as the
  **`X-Ui-Lang`** header (`LocaleService.Localize`). Only dynamic integration-error text (raw Hue/Spotify/
  device messages) stays English.
- **Error localization (`Errors/`)** — user-facing failures carry a stable, machine-readable code so the
  message can be localized and consumers (Stream Deck/MCP/tests) can branch on it. Validation and endpoints
  throw a code-carrying `ValidationException` (400) / `NotConfiguredException` (503)
  ([AppExceptions.cs](src/AmbientDirector.Api/Errors/AppExceptions.cs), both implementing `IErrorCode` with a
  dotted `error.*` `Code` + interpolation `Args`; a `CtxRef` arg is itself a localizable "which light/effect"
  fragment). [`ErrorClassifier`](src/AmbientDirector.Api/Errors/ErrorClassifier.cs) maps every exception →
  (HTTP status, title key), shared by the error middleware and the activators. The middleware localizes the
  ProblemDetails `title`/`detail` via `LocaleService.Localize` and emits `code`/`args` extensions; server logs
  stay English (`Exception.Message` renders English from the embedded `en.json` via `ErrorMessages`, guarded
  so it never throws). Partial-activation statuses fold to `error:<code>` and the panel renders them
  (`UiExtensions.ProblemSummary`). **When adding a failure:** throw a `ValidationException`/`NotConfiguredException`
  with a new `error.*` code and add that key to `en.json` **and** `pl.json`; for a brand-new exception *type*,
  add a `switch` arm to `ErrorClassifier` (not the middleware).
- **`SceneStore` / `SettingsStore`** — persistence (see below).
- **`AiToolService`** ([AiToolService.cs](src/AmbientDirector.Api/Services/Ai/AiToolService.cs)) — the shared
  AI **tool façade** (a singleton) behind both AI surfaces (MCP + the assistant): full CRUD + live control
  over scenes, events, **screens** and Light FX; **Spotify music transport** (`play_music`/`pause_music`/
  `resume_music`/`next_track`/`previous_track`/`set_music_volume`/`set_music_shuffle`/`set_music_repeat`);
  **soundboard control** (`play_sound`/`stop_sound`/`stop_all_sounds`/`update_sound`); the **live table**
  (party players, table-level counters, and the bestiary — `list_party`, `upsert_player`/`delete_player`,
  `save_table_counters`, `adjust_player_counter`/`adjust_table_counter`, `upsert_enemy`/`delete_enemy`/
  `adjust_enemy_counter`); **encounters** (`list_encounters`/`get_encounter`/`upsert_encounter`/
  `delete_encounter`/`run_encounter`/`adjust_encounter_enemy`/`reset_encounter`); **boards** (`list_boards`/
  `get_board`/`upsert_board`/`delete_board`); the **player-facing TV** (`show_on_tv`/`clear_tv`/`get_tv_state`);
  plus read-only context and state (`list_lights`, `list_sounds`, `list_spotify_playlists`,
  `search_spotify_tracks`, `reset_lights`, `get_lights_status`, `get_music_state`, `get_sounds_state`,
  `get_event_state`) — **66 ops total**. Setup/
  secrets/logs/diagnostics stay deliberately excluded. It reuses the exact HTTP paths' machinery: the
  `Validation/*` guards, the stores, and Scene/Event/Screen/Board/Party/Encounter image cleanup (capture old
  `Image`/`Portrait`/`BackgroundImage` or the board file set, upsert, `ImageFileStorage.Delete` on
  replace/delete), **and the same `TvState` side effects** the endpoints emit — party/board/encounter mutations
  call the mirror of `TouchIfPartyShown` / `TouchBoard`/`TouchEncounter` / `ForgetBoard`/`ForgetEncounter`, so an
  open TV re-renders identically whether the change came over HTTP or a tool call. Because
  `SceneActivator`/`EventActivator`/`ILightService`/
  `SpotifyClient` are **scoped**, every op that touches them does `using var scope = scopeFactory.CreateScope()`
  and resolves per call (the `EventTimelineRunner`/`LightFxTester` pattern; music/lights-status use a
  `WithSpotifyAsync`/scoped `ILightService` helper; `run_encounter` runs the scene+event best-effort in their own
  scopes, like the endpoint), while the singleton `SoundboardPlayer`/`PartyStore`/`EncounterStore`/`BoardStore`/
  `TvState` need no scope; `AiJson` gives it a `JsonSerializerDefaults.Web` options so tool JSON matches the wire
  exactly. The FX
  detach-on-delete logic is factored into `LightFxDetacher` ([LightFxDetacher.cs](src/AmbientDirector.Api/Services/LightFxDetacher.cs))
  so the endpoint and the façade delete FX identically. `update_sound` deliberately edits only name/category/
  volume/loop (never the tile art or file); there is deliberately **no** `delete_sound` op (it would have to
  replicate the endpoint's scene/event/timeline scrub). Enemy AI ops act on the **bestiary statblock's base
  stats** (`adjust_enemy_counter`); live per-fight tracking is `adjust_encounter_enemy` on an encounter instance.
- **MCP server** — `ModelContextProtocol.AspNetCore` (stateless streamable HTTP) mapped at **`/mcp`**
  (`AddMcpServer().WithHttpTransport(o => o.Stateless = true)` + `MapMcp("/mcp")` in
  [Program.cs](src/AmbientDirector.Api/Program.cs), before `MapFallbackToFile`). The 66 tools
  ([McpTools.cs](src/AmbientDirector.Api/Services/Ai/McpTools.cs)) are thin `[McpServerTool]` adapters over
  `AiToolService`, grouped into tool-type classes by domain
  (scene/event/screen/lightFx/music/sound/library/party/encounter/board/tv),
  each registered with a `.WithTools<…>()` call in Program.cs (upserts take the typed entity so schemas
  auto-generate from the models). Point Claude Code / Claude Desktop here; it is behind the optional API-key
  gate like the rest of the API. **The MCP surface and the assistant's `AssistantTools.Definitions` must stay
  in lockstep** — every op appears on both, dispatching through the façade; `AiToolSurfaceParityTests` fails
  the build if the two name sets ever diverge.
- **`AssistantService` / `AssistantStore` / `IAssistantProvider`** — the in-panel **BYOK assistant**, which
  runs against **any of three backends** (Anthropic / OpenAI / Gemini), one active at a time. `AssistantStore`
  ([AssistantStore.cs](src/AmbientDirector.Api/Services/AssistantStore.cs)) persists the active `provider` +
  API key + model in SQLite via `/setup/assistant/*` (`AssistantConfig` single row; the key is **never
  echoed** — endpoints return only `{provider, configured, model}`). `AssistantService`
  ([AssistantService.cs](src/AmbientDirector.Api/Services/Ai/AssistantService.cs), a singleton) runs a single
  in-memory chat session as a **provider-agnostic** non-streaming agentic tool loop: it owns the transcript,
  history and cancel/busy state, keeps history in a neutral block model
  ([Providers/AssistantChat.cs](src/AmbientDirector.Api/Services/Ai/Providers/AssistantChat.cs)), and delegates
  each model turn to the configured [`IAssistantProvider`](src/AmbientDirector.Api/Services/Ai/Providers/IAssistantProvider.cs)
  (one adapter each — `AnthropicProvider` / `OpenAiProvider` / `GeminiProvider` — selected per run by
  `AssistantConfig.Provider`, using the official `Anthropic`, `OpenAI` and `Mscc.GenerativeAI` SDKs). Tools
  come from `AssistantTools` as provider-neutral `AiToolDefinition`s (each adapter maps them to its SDK's tool
  type) → the same façade. The panel drives it over `/assistant/*` (`send`/`state`/`stop`/`clear`) by
  **polling `/assistant/state?rev=`** (the codebase's real-time idiom — no SSE). A backend failure surfaces as
  `AiProviderException`, mapped to a `502` arm in the Program.cs error switch. The single conversation is
  **persisted to SQLite** (one `AssistantConversation` row via `AssistantConversationStore`, hydrated lazily on
  first access) so it survives a server restart; `clear` wipes it permanently.
- **Installable Windows build (#75)** — [release-win.yml](.github/workflows/release-win.yml) cross-publishes
  (from an ubuntu runner) a self-contained, single-file **win-x64** exe via the
  [`win-x64` publish profile](src/AmbientDirector.Api/Properties/PublishProfiles/win-x64.pubxml) on a `v*` tag (or
  manual dispatch), packaging `AmbientDirector.Api.exe` + the trimmed Blazor `wwwroot` + starter `scenes.json`
  into `ambient-director-win-x64.zip` as a GitHub release. Program.cs recognizes this build by an **empty
  entry-assembly `Location`** (`isPublishedExe`): it then roots the content root at `AppContext.BaseDirectory`
  (so it runs from any launch dir) and defaults `Launch:OpenBrowser` on. The everyday PR build
  ([build.yml](.github/workflows/build.yml)) is untouched.

### Conventions

- **Every command endpoint accepts both GET and POST** (`EndpointHelpers.GetOrPost`) so the Stream Deck
  *System → Website* action works without a plugin. Keep this when adding command routes.
- **Errors → status codes**: failures are thrown as typed exceptions (`SpotifyException`, `HueException`,
  `ValidationException`/`NotConfiguredException`, socket/timeout, etc.), classified to an HTTP status + title
  key by [`ErrorClassifier`](src/AmbientDirector.Api/Errors/ErrorClassifier.cs), and rendered as localized
  Problem responses (+ a `code`/`args` extension) by the first middleware in
  [Program.cs](src/AmbientDirector.Api/Program.cs). When adding a new failure mode, throw a code-carrying
  `ValidationException`/`NotConfiguredException` (with an `error.*` key added to `en.json`+`pl.json`) rather
  than returning ad-hoc error bodies; for a new exception *type*, add a `switch` arm to `ErrorClassifier`. See
  the error-localization notes under `LocaleService` above.
- **Optional API key**: when `Security:ApiKey` is set, `/scenes /lights /music /sounds /events /screens
  /boards /party /lightfx /images /setup /logs /diagnostics /mcp /assistant /i18n` — plus the TV **push** commands
  `/tv/show*` and `/tv/clear` (the rest of the player-facing `/tv` surface stays open, see `TvState`) — require it
  (`X-Api-Key` header or `?apiKey=`; the Spotify OAuth callback is exempt). The panel stores it in browser
  localStorage. Keep new protected prefixes in `IsProtectedPath` in [Program.cs](src/AmbientDirector.Api/Program.cs).
- **DTOs are duplicated**, not shared: the API's wire DTOs live in `Contracts/` and the UI keeps its own
  copies in [its own `Contracts/`](src/AmbientDirector.Ui/Contracts) (there is no shared contracts project).
  If you change an API DTO, update the matching UI DTO by hand.

## Persistence

Scenes and lighting settings live in **SQLite via EF Core**, not appsettings.json. The DB is at
`%LocalAppData%\AmbientDirector\ambient-director.db` (override with `Database:Path`). Context:
[AppDbContext.cs](src/AmbientDirector.Api/Data/AppDbContext.cs). Tables: `Scenes` (Light/Music stored
as JSON columns; ids use `NOCASE` collation), `Sounds` (soundboard metadata; ids `NOCASE`),
`MusicTracks`/`MusicPlaylists` (local music library; playlist `TrackIds` a JSON column; ids `NOCASE`), `Events`
(one-shot triggered effects; `Flash` and `Timeline` JSON columns; ids `NOCASE`), `Screens` (shortcut boards;
`Tiles` JSON column plus a `Compact` layout flag; ids `NOCASE`), `Boards` (player-facing TV layouts;
background colour/image plus an `Elements` JSON column; ids `NOCASE`), `LightFxs` (reusable Light FX library; `Keyframes` JSON column; ids
`NOCASE`), `PartyMembers` (party-tracker players; name/portrait/sortOrder plus a `Counters` JSON column; ids
`NOCASE`), a
single-row `LightingConfig` (whose `DefaultLight` JSON column backs `/lights/default`, plus a nullable
`OnboardingDoneUtc` stamp for the first-run wizard), a single-row `PartyConfig` (the party tracker's
table-level counters in a `Counters` JSON column — Fear etc. — plus the nullable `SystemId` game-system
choice, see `IGameSystem`), a single-row `FreesoundConfig` (the Freesound API token)
and a single-row
`AssistantConversation` (the in-panel assistant's persisted transcript + history, each a JSON string). The Spotify
connection (Client ID, refresh token, preferred device) is also persisted here via `SpotifyStore`.

Sound-effect **audio files** live on disk, not in the DB, at `%LocalAppData%\AmbientDirector\sounds\`
(override with `Sounds:Path`); each `Sound` row references its file by name via `SoundFileStorage`.
`Scene.SoundEffects` (a `List<string>` JSON column) holds the ids of sounds a scene fires on activation.
Local **music files** live alongside at `%LocalAppData%\AmbientDirector\music\` (override `Music:Path`),
one file per `MusicTrack` via `MusicFileStorage`.

`appsettings.json` holds deployment config only: `Urls`, `Security:ApiKey`, `Database:Path`, `Sounds:Path`,
`Music:Path`, `Images:Path`, `Locales:Path`, `Audio:Backend` (audio sink: `auto` (default) / `waveout` /
`openal` — see the soundboard notes), and `Launch:OpenBrowser` (auto-open the panel at startup — default
on for the installable exe, off under `dotnet run`).

### Changing the schema — create a migration

Any change to `AppDbContext`, the entities
([LightingConfig.cs](src/AmbientDirector.Api/Data/LightingConfig.cs),
[Scene.cs](src/AmbientDirector.Api/Models/Scene.cs)), or their `OnModelCreating` mapping **requires a new
migration** — the app only applies migrations, it never auto-creates tables from the model. After
editing the model, run from the API project:

```
dotnet dotnet-ef migrations add <Name> -o Data/Migrations
```

Commit the generated files under `Data/Migrations/`. Startup runs `Database.Migrate()` automatically,
so no manual `database update`. `dotnet-ef` is a local tool (restored via `dotnet-tools.json`). Do not
hand-edit a committed migration; add a new one instead.

### Legacy import

On first run with an empty DB, [LegacyImporter.cs](src/AmbientDirector.Api/Data/LegacyImporter.cs) imports
the old `scenes.json` and `settings.local.json` once, then never reads them again. `scenes.json` also
serves as the starter scene template on a fresh clone.
