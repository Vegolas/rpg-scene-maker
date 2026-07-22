# 🎲 Ambient Director

A local REST API (C# / .NET 10 Minimal API) **plus a touch control panel (Blazor WASM)** that switches your whole tabletop-RPG mood with one tap — from a Stream Deck, an iPad, or any browser. Lights run **directly over your LAN** (fast, no internet); Spotify is the only cloud dependency, and even that is optional.

- **Lighting** — a Tuya smart bulb (e.g. Polux GU10) **or Philips Hue lights**, controlled locally over the LAN. Pick the system on the panel's ⚙ Settings page — scenes and endpoints are identical for both.
- **Music** — **Spotify** (Premium) via the Spotify Web API driving a Spotify Connect device on your LAN, **or a built-in local music library** (import your own audio and play it on the machine running the app — no Spotify needed).
- **Sound effects** — a built-in **soundboard**: import your own audio (MP3/WAV/OGG) — or search and import Creative-Commons effects from **Freesound** — and fire one-shots or looping ambience. Each sound has its own volume and loop setting.
- **Scenes** — named presets combining light colour/brightness + a Spotify or local playlist/track + sound effects, stored in a local SQLite database.
- **Events** — one-shot stingers (a light flash and/or a sound, e.g. thunder) fired *on top of* the current scene, with an optional video-editor-style **timeline**.
- **Screens & Boards** — **Screens** group your existing scenes/events/sounds/playlists onto tap-friendly GM shortcut boards; **Boards** compose player-facing content (background, images, text, live party/enemy stats) to push to a shared **TV** display.
- **Game layer** — a party tracker, a reusable enemy **bestiary**, prepped **encounters**, and table counters (HP, Stress, Fear, …), all driven by a pluggable **game system** you choose in Settings. See [The game layer](#the-game-layer) below.
- **AI** — an optional bring-your-own-key **assistant** built into the panel, plus an **MCP server** so Claude can build and drive your table's mood.

Every command endpoint accepts **both GET and POST**, so the built-in Stream Deck *System → Website* action works — no plugin required.

https://github.com/user-attachments/assets/05fb2764-3ad1-487f-9dbe-22114ec4d79d

## Platform support

Ambient Director runs on **Windows, Linux and macOS** — every feature is cross-platform.

| Feature | Windows | Linux | macOS |
|---|:---:|:---:|:---:|
| Control panel (Blazor WASM) | ✅ | ✅ | ✅ |
| Lights — Tuya bulb (LAN) | ✅ | ✅ | ✅ |
| Lights — Philips Hue (LAN) | ✅ | ✅ | ✅ |
| Music — Spotify | ✅ | ✅ | ✅ |
| Music — local library | ✅ | ✅ | ✅ |
| Soundboard | ✅ | ✅ | ✅ |
| Party / bestiary / encounters / boards | ✅ | ✅ | ✅ |
| AI assistant + MCP server | ✅ | ✅ | ✅ |
| Double-click packaged build | ✅ | — | — |

Lights, Spotify and the panel are plain HTTP/TCP, so they were always portable. The soundboard and the local-music library play on the machine running the app through NAudio's managed mixing graph, with the audio **output** device coming from a pluggable sink — NAudio's `WaveOutEvent` on Windows and a bundled OpenAL Soft sink on Linux/macOS. A host with no audio device just degrades that one feature (a 503) instead of crashing.

## Get it running

The API and the control panel are a single process — running the API serves the panel too. With the [.NET 10 SDK](https://dotnet.microsoft.com/download) installed, on any OS:

```powershell
dotnet run --project src/AmbientDirector.Api
```

This serves everything on **http://localhost:5252** (and across your LAN — see [Using it from an iPad](#using-it-from-an-ipad-or-any-tabletphone)). On first launch a **guided setup** walks you through picking your lights, connecting music, and a couple of optional extras — every step is skippable, so the app is usable straight away with the starter scenes.

The panel's tabs: **Scenes** (one-tap presets with a live active highlight), **Screens** (custom GM shortcut boards), **Music** (Spotify + a local library — now-playing, transport, volume, shuffle/repeat, playlists, search), **Lights** (colour, brightness, white temperature, per-light targeting), **Sounds** (import or search Freesound + soundboard), **Events** (one-shot flash + sound stingers, with a timeline), **Effects** (reusable Light FX library), **Boards** (compose player-facing TV content), **Encounters** (party tracker, bestiary and prepped fights — hidden until you pick a game system), and **TV** (push content to a shared screen). An **Assistant** tab appears once you configure an AI key, and a **Logs** tab in developer mode. Everything else lives under **⚙ Settings**.

### Packaged Windows build (double-click, no SDK)

A self-contained, single-file **win-x64** build is produced by the [`release-win` workflow](.github/workflows/release-win.yml) when a `v*` tag is pushed, and attached as `ambient-director-win-x64.zip` on the repo's [Releases page](https://github.com/Vegolas/ambient-director/releases). Download it, unzip anywhere (Desktop/Documents is fine), and double-click **`AmbientDirector.Api.exe`**. A small console window shows where the panel is running and your browser opens automatically:

```
Ambient Director is running.
  On this PC:      http://localhost:5252
  On your network: http://192.168.1.20:5252
  Close this window to stop the server.
```

> **"Windows protected your PC"?** The exe is a self-published open-source build without a code-signing certificate, so Windows SmartScreen will likely show that warning the first time you run it. Click **More info → Run anyway** to start it.

> **No release posted yet?** Until a `v*` tag is cut, run from source (above), or grab a ready-made build (win-x64 / linux-x64 / osx-arm64) from the latest [`publish` CI run](.github/workflows/publish.yml)'s artifacts.

- **Your data** (scenes, settings, imported sounds/music, images) lives under `%LocalAppData%\AmbientDirector\`, separate from the app folder — so you can drop a newer build in place and keep everything.
- **Keep the window open** while you play; closing it stops the server. On first run Windows may ask to allow network access — allow it for **private networks** so tablets on your Wi-Fi can reach the panel.
- Running headless and don't want the browser to pop up? Set `"Launch": { "OpenBrowser": false }` in the `appsettings.json` next to the exe (or pass `--Launch:OpenBrowser=false`).

<details>
<summary><b>Self-contained builds for Linux / macOS (no SDK on the target)</b></summary>

Publish a self-contained single file for your OS (the panel's `wwwroot` and starter `scenes.json` land next to it):

```bash
# Linux (x64)
dotnet publish src/AmbientDirector.Api -p:PublishProfile=linux-x64 -o publish/linux-x64
# macOS (Apple Silicon)
dotnet publish src/AmbientDirector.Api -p:PublishProfile=osx-arm64 -o publish/osx-arm64
```

Then `cd publish/<rid>` and run `./AmbientDirector.Api`. These same artifacts (plus `win-x64`) are also produced by the [`publish` CI workflow](.github/workflows/publish.yml) on every push to `main`, so you can download a ready-made `.tar.gz` from a run's summary instead of building it yourself.

> **macOS Gatekeeper:** the binary isn't signed/notarized, so first-run may be blocked. Clear the quarantine flag with `xattr -dr com.apple.quarantine AmbientDirector.Api` (or right-click → Open once).

> **Building in a slim/Alpine container?** The SCSS is compiled by `AspNetCore.SassCompiler`, whose `sass` binary is glibc-linked. On Alpine (musl) add the `gcompat` package (`apk add gcompat`) or the build fails with a "not found" error running `sass`. The regular Debian-based .NET SDK images need nothing extra.

</details>

## Using it from an iPad (or any tablet/phone)

<details>
<summary><b>Open the panel on a tablet and add it to the home screen</b></summary>

1. Run the API on the host machine, and make sure its firewall allows inbound TCP on port **5252** for your local network (the app already binds all interfaces — see `Urls` under [Configuration](#configuration-appsettingsjson)):
   - **Windows** — the first `dotnet run` pops a prompt; **allow it for private networks**. If you skipped it: `netsh advfirewall firewall add rule name="Ambient Director" dir=in action=allow protocol=TCP localport=5252`
   - **macOS** — the application firewall is off by default; if you've turned it on (System Settings → Network → Firewall), allow `AmbientDirector.Api` (or `dotnet`) when macOS prompts on first launch.
   - **Linux** — most desktop distros ship with no firewall enabled. If yours is on, open the port: `sudo ufw allow 5252/tcp` (ufw) or `sudo firewall-cmd --add-port=5252/tcp --permanent && sudo firewall-cmd --reload` (firewalld).
2. Find the host's LAN address — `ipconfig` on Windows, `ip addr` (or `ifconfig`) on macOS/Linux → the IPv4 like `192.168.1.20`. A DHCP reservation for the machine keeps the address stable.
3. On the iPad open Safari → `http://192.168.1.20:5252`, then **Share → Add to Home Screen**. The panel is an installable PWA — it gets its own icon and launches fullscreen like a native app, with no Safari chrome.
4. Recommended: set an API key (see [Security](#configuration-appsettingsjson)), then enter the same key on the iPad via the ⚙ button — it is stored on the device.
5. Table tip: Settings → Display & Brightness → Auto-Lock → Never (or use Guided Access) so the panel doesn't sleep mid-session.

> **Installing on Android / a desktop browser:** Chrome and Edge show an **Install** button in the address bar, but only on a *secure* origin — `http://localhost:5252` on the PC itself, or the panel served over HTTPS. Over a plain `http://` LAN address they won't offer install (a browser rule, not an app limit); use the iPad's **Add to Home Screen** above, which works over LAN http.

</details>

## The game layer

Beyond lights and sound, Ambient Director tracks the *game* at your table — and adapts to the system you play.

- **Game system** — pick your system under **⚙ Settings → Game system**. It unlocks the **Encounters** tab and tailors the counter presets and the player-facing stat display. **Daggerheart** ships built-in (HP / Stress / Armor / Hope, plus table-wide **Fear**); a deliberately minimal **D&D 5e** sample is included too. No system chosen → the Encounters tab is hidden and nothing else changes. New systems are added with a small code contribution — a class, its labels and one registration line, reviewed by PR (no plugins). See **[docs/GAME-SYSTEMS.md](docs/GAME-SYSTEMS.md)**.
- **Party tracker** — a card per player with a portrait and counters (HP, Stress, …). Tap **+ / −** to adjust mid-session; a shared party board on the TV updates live. Table-wide stats such as Daggerheart's **Fear** get a **quick control in the top bar**, reachable from every tab.
- **Bestiary** — reusable enemy statblocks (name, portrait, base counters) you build once and drop into encounters.
- **Encounters** — prepped fights: pick your heroes and enemy instances, then **Run** to push the scene to the TV. Track each enemy's counters live during the fight — and the players only ever see *marked* damage on the TV, never an enemy's max, so they can't tell how close it is to dropping.
- **Boards** — compose what the table sees: a fixed 16:9 stage with a background colour or image, positioned images, text, and live **party / enemy** stat panels. Push a board to the TV from the Boards tab or the TV remote; edits to a shown board appear on the TV within a couple of seconds.

*(The party tracker, bestiary and encounters all live under the **Encounters** tab; boards have their own **Boards** tab.)*

## One-time setup

Everything here is optional and reachable from the first-run wizard or **⚙ Settings** later. Expand the piece you need:

<details>
<summary><b>1a. Tuya bulb (local control)</b> — skip if you use Hue</summary>

You need three values on the panel's ⚙ Settings page: the bulb's **IP**, **device id** and **local key**.

**IP + device id** — with the API running, call:

```
http://localhost:5252/setup/scan?seconds=10
```

Tuya devices broadcast on the LAN every few seconds; the response lists `ip`, `deviceId` and the protocol version. (Windows Firewall may ask to allow the app — accept for private networks.)

**Local key** — a one-time extraction via a free Tuya developer account:

1. Sign up at [iot.tuya.com](https://iot.tuya.com) → **Cloud → Create Cloud Project** (choose the **Central Europe** data center for Poland; select the *Smart Home* / *IoT Core* API).
2. In the project: **Devices → Link App Account** → scan the QR code with your **Smart Life / Tuya Smart** app (the app your bulb is paired with). Your bulb appears in the device list.
3. Copy the project's **Access ID** and **Access Secret** (Overview tab), then call:

```
http://localhost:5252/setup/local-keys?accessId=YOUR_ACCESS_ID&apiSecret=YOUR_SECRET&deviceId=YOUR_DEVICE_ID&region=eu
```

The response contains the `localKey` for every device on your account. Save the IP, device id and local key on the ⚙ Settings page — it applies immediately, no restart. The cloud account is **only needed for this step** — all runtime control is local.

> **Old bulb?** If `GET /lights/status` shows data-point keys `1, 2, 3, 5` instead of `20, 21, 22, 24`, set the DP profile to `v1` on the Settings page. If the bulb never responds, try protocol version `3.1`.

</details>

<details>
<summary><b>1b. Philips Hue (local control)</b></summary>

Everything happens in the control panel — open **⚙ Settings**:

1. Pick **Philips Hue** as the light system.
2. Enter the bridge IP (or tap *Find* to auto-discover it).
3. **Press the round link button on the Hue Bridge**, then tap *Pair with bridge* within 30 seconds. The app key is created and saved automatically.
4. Tick the lights the panel and scenes should control (none ticked = all lights), tap *Save Hue settings*, then *Test (toggle)* to confirm.

Settings changed in the panel are saved to the SQLite database and apply immediately — no restart. The raw endpoints (`/setup/hue/discover`, `/setup/hue/register`, `/setup/hue/lights`, `GET|PUT /setup/config`) remain available for scripting.

Tip: give the bridge a fixed IP (DHCP reservation in your router) so the saved address doesn't go stale.

</details>

<details>
<summary><b>2. Music: Spotify and/or a local library</b></summary>

Spotify has no local API, so this uses the Spotify Web API to remote-control a **Spotify Connect** device on your LAN (a phone, desktop app, speaker, etc.). **Spotify Premium is required** for playback control. Setup is a one-time OAuth connect from the control panel:

1. Go to [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) → **Create app**. Give it any name, tick the **Web API**, and set the **Redirect URI** to exactly:

   ```
   http://127.0.0.1:5252/setup/spotify/callback
   ```

   (Spotify only allows `http` on loopback addresses — use `127.0.0.1`, not `localhost` or your LAN IP.) Save, then copy the app's **Client ID** (no client secret is needed — this uses PKCE).
2. Open the panel's **⚙ Settings → Spotify**, paste the Client ID and tap **Save Client ID**. The exact Redirect URI to register is shown there too.
3. **Connect from a browser on the PC running the server** (`http://localhost:5252` or `http://127.0.0.1:5252` — either works; the server always sends the registered `127.0.0.1` form to Spotify) — tap **Connect Spotify** and approve the consent screen. You are redirected back and the panel shows *Connected*.
4. Optionally pick a **playback device** from the dropdown (Refresh lists your currently reachable Spotify Connect devices). Leave it on *Active device* to target whatever is playing.

Once connected, Spotify plays from the **Music** tab (playlist list + track search) and from scenes: a scene's `music.playId` — or `/music/play?id=…` — takes a Spotify URI or link, e.g. `spotify:playlist:37i9dQZF1DX…` or `https://open.spotify.com/playlist/37i9dQZF1DX…`.

Stream Deck example (System → Website action):

```
http://localhost:5252/music/play?id=spotify:playlist:37i9dQZF1DX8NTLI2TtZa6
```

**Troubleshooting**

- *"No active Spotify device"* — Spotify must be **open** somewhere (the desktop app, phone, or a speaker). Open it, tap Refresh next to the device dropdown in Settings, and pick it as the playback device — then scenes can wake it even when nothing is playing.
- *"Spotify Premium is required"* — playback control over the Web API is a Premium-only feature; free accounts can't be remote-controlled.
- *"redirect_uri: Not matching configuration" / connect loops back with an error* — the Redirect URI in the Spotify dashboard must match the one shown in Settings **character for character** (no trailing slash, `http` not `https`, port included), and the connect must be started from the server PC — not from the iPad/LAN address, which Spotify won't accept over http.
- *Worked for weeks, now "Spotify error"* — the saved token can be revoked (password change, "remove access" in your Spotify account). Tap **Disconnect** and connect again.

**Local music library (no account needed)**

Prefer your own files, or don't have Spotify? Import audio straight into the app on the **Music** tab → *Import audio* (MP3/WAV/OGG). Tracks play on the **machine running the app** via NAudio (cross-platform, like the soundboard), on their own output device independent of the soundboard mixer. Group tracks into **local playlists**, and point a scene at one with a `local:track:…` or `local:playlist:…` id — the Music tab and scene editor fill these in for you. `/music/play?id=…` accepts the same ids, and the shared transport (pause/resume/next/volume/shuffle/repeat) targets whichever source is currently playing.

</details>

<details>
<summary><b>3. Stream Deck</b></summary>

Use the built-in **System → Website** action (untick "Open in browser" / GET is fine), or the *Web Requests* plugin for POST:

| Button | URL |
|---|---|
| Tavern scene | `http://localhost:5252/scenes/tavern/activate` |
| Combat scene | `http://localhost:5252/scenes/combat/activate` |
| Play playlist | `http://localhost:5252/music/play?id=spotify:playlist:37i9dQZF1DX8NTLI2TtZa6` |
| Pause music | `http://localhost:5252/music/pause` |
| Light toggle | `http://localhost:5252/lights/toggle` |
| Dim to 20% | `http://localhost:5252/lights/brightness?value=20` |
| Play a sound | `http://localhost:5252/sounds/thunder/play` |
| Stop all sounds | `http://localhost:5252/sounds/stop` |
| Mark −1 Fear | `http://localhost:5252/party/counters/adjust?counter=fear&delta=-1` |

Counter adjusts prefer the stable semantic **key** (`counter=fear`), so the same button works regardless of the panel's language.

</details>

<details>
<summary><b>4. AI assistant (optional, bring-your-own-key)</b></summary>

Ambient Director can hand its scenes, events, sounds, boards and the whole game layer to an AI — either as a chat panel built into the app, or as an **MCP server** you point Claude Code / Claude Desktop at. Both are **bring-your-own-key**: you use your own provider API key, and all model usage is billed to *your* account. The in-panel chat works with **Anthropic (Claude), OpenAI (GPT) or Google Gemini** — one active at a time; the MCP server targets Claude clients.

**In-panel chat**

1. Get an API key: [console.anthropic.com](https://console.anthropic.com) (Anthropic), [platform.openai.com](https://platform.openai.com) (OpenAI), or [ai.google.dev](https://ai.google.dev) (Gemini). Set a spend limit there if you like.
2. Open the panel's **⚙ Settings → AI Assistant**, pick the **Provider**, paste that provider's key, set a **Model** (e.g. `claude-opus-4-8`, `gpt-4o`, `gemini-2.0-flash`), and tap **Save assistant settings**. The key is stored on the server and never shown again; leave the field blank on later saves to keep it while changing the provider/model.
3. Chat from the **🤖 Assistant** tab: *"create a spooky crypt scene with dim purple lights and ambient music"*, *"add a thunder event with a white flash and my thunderclap sound"*, *"build a Horror screen with my crypt scene and thunder event"*, or *"add a goblin to the bestiary and put three of them in a new encounter"*.

**MCP server (Claude Code / Claude Desktop)**

The app hosts an MCP endpoint at **`/mcp`** with the same 60-plus tools. Point a client at it:

```
claude mcp add --transport http ambient-director http://localhost:5252/mcp
```

If you've set a panel API key (`Security:ApiKey`), send it as a header:

```
claude mcp add --transport http ambient-director http://localhost:5252/mcp --header "X-Api-Key: your-key"
```

For **Claude Desktop**, add it to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "ambient-director": {
      "type": "http",
      "url": "http://localhost:5252/mcp",
      "headers": { "X-Api-Key": "your-key" }
    }
  }
}
```

(Drop the `headers` block when no panel API key is configured.) The tools cover full CRUD plus live control — activate a scene, trigger an event, test a light effect, run an encounter, adjust a counter, push a board to the TV — so you can build and drive your table straight from a Claude conversation.

</details>

<details>
<summary><b>5. Obsidian (optional)</b></summary>

Run scenes straight from your session notes. The [Obsidian plugin](integrations/obsidian/) turns an inline token like `` `sm:scene:city` `` into a clickable button — tap it while you read to switch the table's lights + music, without leaving the note. It autocompletes the scenes/events/sounds that exist on your server (with their art) and fires them in the background over the same GET endpoints. Build and install it from [`integrations/obsidian/`](integrations/obsidian/README.md); set your server address (and API key, if any) in its settings.

**[It's now available in the official Obsidian community plugins list](https://community.obsidian.md/plugins/ambient-director)**

https://github.com/user-attachments/assets/38f4f217-9fa5-43f2-8306-23a74364a963

</details>

## Scenes

Manage scenes from the panel's Scenes tab or with `PUT /scenes/{id}`:

```json
{
  "id": "combat",
  "name": "⚔️ Combat",
  "light": { "power": true, "color": "#FF1E1E", "brightness": 100 },
  "music": { "playId": "spotify:playlist:37i9dQZF1DX8NTLI2TtZa6", "volume": 0.7 }
}
```

- `light`: `color` (hex) **or** white via `brightness` + `temperature` (0 = warm, 100 = cold); `power: false` turns it off.
- `music`: `playId` starts a playlist/track/album/artist — a `spotify:` URI or `open.spotify.com` link (a pasted share link works as-is), or a local-library `local:track:…` / `local:playlist:…` id; `volume` is 0–1, or `"pause": true` to stop playback. Add `"source": "spotify"` / `"local"` to force a source (otherwise it's inferred from the id).
- `soundEffects`: a list of sound ids (from the Sounds tab) to fire — e.g. `"soundEffects": ["thunder", "rain"]`.
- Any part can be omitted — light, music and sound effects are applied concurrently, and the response reports each part separately (HTTP 207 if something failed).

## Soundboard

Import your own sound effects and fire them from the panel's **Sounds** tab or a Stream Deck. Audio plays on the **machine running the app** — so it comes out of the same speakers no matter which panel or button triggered it, including scene activations.

- **Import** — Sounds tab → *Import a sound* → an **MP3, WAV or OGG** file, stored under `%LocalAppData%\AmbientDirector\sounds`.
- **Tune** — each sound has a **volume** and a **loop** toggle (one-shot vs. continuous ambience). Tap ✎ to rename it, set its category, adjust volume/looping, and *Preview*.
- **Play** — tap a sound to play, tap again to stop. Sounds **overlap** (thunder over rain); **Stop all** stops everything.
- **From scenes** — pick which sounds a scene fires in the scene editor's *Sound Effects* section.
- **Freesound library** — no files of your own? Tap *Search library* to search [Freesound](https://freesound.org)'s Creative-Commons library and import a result in one tap (the author/licence attribution is kept). It needs a free token — create one at [freesound.org/apiv2/apply](https://freesound.org/apiv2/apply) and paste it under **⚙ Settings → Sound library (Freesound)**.

## Events

**Events** are one-shot triggered effects — a brief light **flash** and/or a **sound**, fired *on top of* the current scene instead of replacing it. The classic example is **thunder**: a white flash + a thunderclap, after which the lights return to whatever scene was live. Manage them from the **Events** tab or with `PUT /events/{id}`:

```json
{
  "id": "thunder",
  "name": "⚡ Thunder",
  "flash": { "color": "#FFFFFF", "brightness": 100, "durationMs": 200 },
  "soundEffects": ["thunderclap"]
}
```

- `flash`: the lights jump to `color` at `brightness` (0–100), hold for `durationMs`, then restore the **live scene's** lighting — or the configured default lighting if no scene is active. Omit for a sound-only event.
- `soundEffects`: sound ids played **over** whatever is already playing. Omit for a flash-only event.
- Fire with `GET|POST /events/{id}/trigger`. An event can also carry a **timeline** — sound and light *clips* placed at millisecond offsets, edited in a video-editor-style track — for a scripted sequence rather than a single flash.

## Tile art

Scenes, events, screens and sounds can each carry a background image on their tile. In any editor, tap **Upload art** to crop and upload your own picture, **Search art** to find one without leaving the panel (type a query, pick a thumbnail, and the server imports it), or **From PDF** to import a page from a PDF handout.

The built-in search source is [Scryfall](https://scryfall.com)'s public API — no key or account needed, though it does need internet access (everything else at runtime stays on your LAN). The results are *Magic: The Gathering* card art: **© Wizards of the Coast, served via Scryfall**. That's fine for personal use at your own table — please don't redistribute it.

## TV display & boards

Push content to a **shared table screen** without leaving the panel. Open **`http://<your-pc>:5252/tv`** on whatever the table sees (a TV's browser, a spare tablet, a cast tab); it shows only what you push, full-screen.

- **Images** — from the **TV** tab, upload or pick an image (a map, a handout, an NPC portrait) and tap *Show*, *Clear* to blank it, or re-show something from the recent list.
- **Boards** — from the **Boards** tab, compose a 16:9 layout (background, images, text, and live party/enemy stat panels) and push it to the same screen. A shown board re-renders on the TV within a couple of seconds when you edit it or adjust a counter.

The player display (`/tv`, `/tv/state`, `/tv/content/*`) stays **outside the API-key gate** so a shared screen never needs the admin key — the only key-free data is what you deliberately pushed (the current image, or the files and live party portraits of the currently-shown board). The GM push commands (`/tv/show`, `/tv/clear`) are gated like the rest of the API.

## Language & translations

The panel's language is switched at runtime from **Settings → Language** (saved per device, so each tablet/PC can differ). English and Polish ship in the box.

<details>
<summary><b>Adding or editing a language</b></summary>

Translations are plain JSON files on the server, so anyone — including an AI agent — can add or edit a language without touching code or rebuilding:

- Files live in `%LocalAppData%\AmbientDirector\locales\` (override with `Locales:Path`). `en.json` and `pl.json` are written there on first run.
- To **add a language**, copy `en.json` to `<code>.json` (a [BCP-47](https://en.wikipedia.org/wiki/IETF_language_tag) code — `de`, `fr`, `es`, `pt-BR`, …), set `name` to the language's own name (e.g. `Deutsch`), and translate the values under `strings`. It shows up in the picker after a reload (files are read on demand — no restart needed).
- Any key you leave out falls back to English, so a partial translation is fine.

  ```json
  {
    "name": "Deutsch",
    "englishName": "German",
    "strings": {
      "nav.scenes": "Szenen",
      "common.save": "Speichern"
    }
  }
  ```

- Keep any `{0}` placeholders in a value. Count-dependent keys come in `.one`/`.other` variants (plus `.few`/`.many` for languages such as Polish that need them).
- English also ships embedded in the app, so deleting or corrupting a file on disk can never blank the panel.

> Coded server-side error and validation messages are localized to the panel's language too (the panel sends it as the `X-Ui-Lang` header). Only raw text passed straight through from an upstream device or service — a Hue/Spotify/Freesound error string — stays in English.

</details>

## Endpoint reference

All command endpoints accept GET or POST; parameters go in the query string.

<details>
<summary><b>Full endpoint table</b></summary>

| Area | Endpoints |
|---|---|
| Scenes | `GET /scenes`, `GET /scenes/active`, `GET/PUT/DELETE /scenes/{id}`, `GET\|POST /scenes/{id}/activate` |
| Lights | `/lights/on`, `/lights/off`, `/lights/toggle`, `/lights/color?hex=FF8C2A&brightness=80`, `/lights/white?brightness=80&temperature=30`, `/lights/brightness?value=50`, `/lights/default` (reset to the configured default state), `GET /lights/status`, `GET /lights/list`, per-light `/lights/{key}/on\|off\|color\|white\|brightness` |
| Music | `/music/play?id=…` (a `spotify:` URI / `open.spotify.com` link **or** a local `local:track:…` / `local:playlist:…` id), `/music/pause`, `/music/resume`, `/music/next`, `/music/previous`, `/music/volume?value=0.5`, `/music/shuffle?value=true`, `/music/repeat?mode=off\|track\|playlist`, `GET /music/playlists`, `GET /music/search?q=…`, `GET /music/state` |
| Music library (local) | `GET /music/library/tracks`, `POST /music/library/import` (multipart), `PUT\|DELETE /music/library/tracks/{id}`, `GET /music/library/playlists`, `PUT\|DELETE /music/library/playlists/{id}` |
| Sounds (soundboard) | `GET /sounds/list`, `POST /sounds/import` (multipart), `PUT\|DELETE /sounds/{id}`, `/sounds/{id}/play?volume=0.8`, `/sounds/{id}/stop`, `/sounds/stop` (all), `GET /sounds/state`, `GET /sounds/library/search?q=…`, `POST /sounds/library/import` (Freesound) |
| Events | `GET /events/list`, `GET/PUT/DELETE /events/{id}`, `GET\|POST /events/{id}/trigger`, `GET\|POST /events/{id}/stop`, `GET /events/state` |
| Light FX | `GET /lightfx/list`, `PUT\|DELETE /lightfx/{id}`, `GET\|POST /lightfx/{id}/test`, `GET\|POST /lightfx/test/stop` |
| Screens | `GET /screens/list`, `PUT\|DELETE /screens/{id}` |
| Boards | `GET /boards/list`, `PUT\|DELETE /boards/{id}` |
| Game systems | `GET /systems/list`, `GET\|POST /systems/current?id=<id\|none>` |
| Party (tracker + bestiary) | `GET /party/list`, `PUT\|DELETE /party/players/{id}`, `PUT /party/counters`, `GET\|POST /party/players/{id}/adjust?counter=&delta=` (or `&value=`), `GET\|POST /party/counters/adjust?counter=&delta=`, `PUT\|DELETE /party/enemies/{id}`, `GET\|POST /party/enemies/{id}/adjust?counter=&delta=` |
| Encounters | `GET /encounters/list`, `PUT\|DELETE /encounters/{id}`, `GET\|POST /encounters/{id}/run`, `GET\|POST /encounters/{id}/reset`, `GET\|POST /encounters/{id}/enemies/{instanceId}/adjust?counter=&delta=` |
| TV display | `GET /tv/state`, `GET /tv/content/current`, `GET /tv/content/board/{name}`, `GET\|POST /tv/show?image=…` \| `?board=…` \| `?encounter=…` (`&label=…`), `GET\|POST /tv/clear`, `GET /tv/show/recent` |
| Images | `POST /images/upload` (multipart), `GET /images/{name}`, `GET /images/sources`, `GET /images/search?source=&q=`, `POST /images/import`, PDF page import (`POST /images/pdf/upload`, `GET /images/pdf/{id}/thumb/{page}`, `POST /images/pdf/{id}/import`) |
| Setup (Tuya) | `GET /setup/scan?seconds=10`, `GET /setup/local-keys?accessId=…&apiSecret=…&deviceId=…&region=eu` |
| Setup (Hue) | `GET /setup/hue/discover`, `GET /setup/hue/register?bridgeIp=…`, `GET /setup/hue/lights` |
| Setup (Spotify) | `GET/PUT /setup/spotify/config`, `GET /setup/spotify/login`, `GET /setup/spotify/callback`, `GET /setup/spotify/devices`, `GET\|POST /setup/spotify/disconnect` |
| Setup (config) | `GET /setup/config`, `PUT /setup/config` — read/update provider + Hue/Tuya settings at runtime (persisted to the database) |
| Setup (assistant) | `GET/PUT /setup/assistant/config` (BYOK provider + key + model; the key is never echoed back), `GET\|POST /setup/assistant/disconnect` |
| Setup (Freesound) | `GET/PUT /setup/freesound/config` (BYO API token; the token is never echoed back), `GET\|POST /setup/freesound/disconnect` |
| Setup (onboarding) | `GET /setup/onboarding` (should the first-run wizard show?), `GET\|POST /setup/onboarding/done` |
| Assistant (chat) | `POST /assistant/send`, `GET /assistant/state?rev=…`, `GET\|POST /assistant/stop`, `GET\|POST /assistant/clear` |
| MCP | `/mcp` — Model Context Protocol server (**60-plus tools** over scenes, events, screens, boards, light FX, the party/bestiary/encounters, the TV, plus music & sound control) for Claude Code / Claude Desktop |
| Translations | `GET /i18n/list` (available languages), `GET /i18n/{code}` (one language's strings) |
| Logs | `GET /logs/list`, `GET\|POST /logs/clear` |
| Diagnostics | `GET /diagnostics` |

</details>

## Persistence

Scenes, lighting settings (provider, Hue, Tuya), the Spotify connection (Client ID, refresh token, preferred device), the local-music/soundboard metadata, screens, boards, the **party / bestiary / encounters** and their table counters, the chosen **game system**, and your **Freesound** and **AI-assistant** API tokens all live in a SQLite database at `%LocalAppData%\AmbientDirector\ambient-director.db` (override with `Database:Path`). Imported audio (sounds + local music) and uploaded tile-art images live on disk next to it under `sounds\`, `music\` and `images\`. The schema is created/upgraded automatically at startup via EF Core migrations. Note the Spotify token and API keys in that file grant access to your accounts — treat backups accordingly.

On first run with an empty database, the legacy JSON files are imported once: `scenes.json` (also the starter template on a fresh clone) and `settings.local.json` (the pre-SQLite settings overlay). After that the database is the source of truth — the legacy files are never read again and can be kept as a backup or deleted.

## Configuration ([appsettings.json](src/AmbientDirector.Api/appsettings.json))

Deployment-level config only — everything else is managed from the panel and stored in the database.

| Key | Meaning |
|---|---|
| `Urls` | Listen address, default `http://0.0.0.0:5252` so tablets on your Wi-Fi can reach the panel. Change to `http://localhost:5252` to lock it to this PC. |
| `Security:ApiKey` | Optional shared secret. When set, all control endpoints require it (`X-Api-Key` header or `?apiKey=`); the panel asks for it under ⚙. The key-free player TV surface is exempt (see [TV display](#tv-display--boards)). |
| `Database:Path` | SQLite file location, default `%LocalAppData%\AmbientDirector\ambient-director.db`. |
| `Sounds:Path` | Folder for imported sound-effect audio files, default `%LocalAppData%\AmbientDirector\sounds`. |
| `Music:Path` | Folder for imported local-music audio files, default `%LocalAppData%\AmbientDirector\music`. |
| `Images:Path` | Folder for uploaded tile-art images, default `%LocalAppData%\AmbientDirector\images`. |
| `Locales:Path` | Folder for UI translation JSON files, default `%LocalAppData%\AmbientDirector\locales`. |
| `Audio:Backend` | Soundboard output sink: `auto` (default — WaveOut on Windows, OpenAL elsewhere), `waveout`, or `openal`. |
| `Launch:OpenBrowser` | Auto-open the panel in your default browser at startup. Defaults **on** for the double-clickable Windows build, **off** under `dotnet run`. |

> ⚠️ The API listens on your whole LAN by default so the iPad can reach it. On a home network the worst case is someone toggling your lights, but set `Security:ApiKey` anyway — one line of config, and the panel + Stream Deck both support it. Never expose the port to the internet.
## Contributing / Development

If you'd like to contribute to Ambient Director, please read the
[CONTRIBUTING.md](CONTRIBUTING.md) guide.

### Build

Run the test suite before submitting a pull request:

```bash
dotnet test
```

For development setup, coding standards, and contribution guidelines, see
[CONTRIBUTING.md](CONTRIBUTING.md).