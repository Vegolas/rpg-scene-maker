# 🎲 RPG Scene Maker

A local REST API (C# / .NET 10 Minimal API) **plus a touch control panel (Blazor WASM)** that switches your whole table mood with one tap — from a Stream Deck, an iPad, or any browser:

- **Lighting** — a Tuya smart bulb (e.g. Polux GU10) **or Philips Hue lights**, controlled **directly over your LAN** (fast, works without internet). Pick the system on the panel's ⚙ Settings page — scenes and endpoints are identical for both.
- **Music** — **Spotify** (Premium) via the Spotify Web API, driving a Spotify Connect device on your LAN.
- **Sound effects** — a built-in **soundboard**: import your own audio (MP3/WAV/OGG) and fire one-shots or looping ambience, played on the machine running the app. Each sound has its own volume and loop setting, and scenes can trigger sounds too.
- **Scenes** — named presets combining light color/brightness + a Spotify playlist/track + sound effects, stored in a local SQLite database.

Every command endpoint accepts **both GET and POST**, so the built-in Stream Deck *System → Website* action works — no plugin required.

## Running

```powershell
dotnet run --project src/RpgSceneMaker.Api
```

This serves both the API and the control panel on **http://localhost:5252** (and on your LAN — see the iPad section). The panel's tabs: **Scenes** (one-tap presets with live active highlight), **Music** (Spotify now-playing, transport, volume, shuffle/repeat, playlist list and track search), **Lights** (mood colors, brightness, white temperature), **Sounds** (import + soundboard), and **Logs**.

## Using it from an iPad (or any tablet/phone)

1. Run the API on your PC. The first `dotnet run` after this change makes Windows ask about network access — **allow it for private networks**. (If you skipped the prompt: `netsh advfirewall firewall add rule name="RPG Scene Maker" dir=in action=allow protocol=TCP localport=5252`.)
2. Find your PC's LAN address: `ipconfig` → IPv4, e.g. `192.168.1.20`. A DHCP reservation for the PC keeps the address stable.
3. On the iPad open Safari → `http://192.168.1.20:5252`, then **Share → Add to Home Screen**. It launches fullscreen like a native app.
4. Recommended: set an API key (see Security below), then enter the same key on the iPad via the ⚙ button — it is stored on the device.
5. Table tip: Settings → Display & Brightness → Auto-Lock → Never (or use Guided Access) so the panel doesn't sleep mid-session.

## One-time setup

### 1a. Tuya bulb (local control)

*Skip this if you use Philips Hue — see 1b.*

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

### 1b. Philips Hue (local control)

Everything happens in the control panel — open **⚙ Settings**:

1. Pick **Philips Hue** as the light system.
2. Enter the bridge IP (or tap *Find* to auto-discover it).
3. **Press the round link button on the Hue Bridge**, then tap *Pair with bridge* within 30 seconds. The app key is created and saved automatically.
4. Tick the lights the panel and scenes should control (none ticked = all lights), tap *Save Hue settings*, then *Test (toggle)* to confirm.

Settings changed in the panel are saved to the SQLite database (see Persistence below) and apply immediately — no restart. The raw endpoints (`/setup/hue/discover`, `/setup/hue/register`, `/setup/hue/lights`, `GET|PUT /setup/config`) remain available for scripting.

Tip: give the bridge a fixed IP (DHCP reservation in your router) so the saved address doesn't go stale.

### 2. Spotify (music)

Spotify has no local API, so this uses the Spotify Web API to remote-control a **Spotify Connect** device on your LAN (a phone, desktop app, speaker, etc.). **Spotify Premium is required** for playback control. Setup is a one-time OAuth connect from the control panel:

1. Go to [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) → **Create app**. Give it any name, tick the **Web API**, and set the **Redirect URI** to exactly:

   ```
   http://127.0.0.1:5252/setup/spotify/callback
   ```

   (Spotify only allows `http` on loopback addresses — use `127.0.0.1`, not `localhost` or your LAN IP.) Save, then copy the app's **Client ID** (no client secret is needed — this uses PKCE).
2. Open the panel's **⚙ Settings → Spotify**, paste the Client ID and tap **Save Client ID**. The exact Redirect URI to register is shown there too.
3. **Connect from a browser on the PC running the server** (`http://localhost:5252` or `http://127.0.0.1:5252` — either works; the server always sends the registered `127.0.0.1` form to Spotify) — tap **Connect Spotify** and approve the Spotify consent screen. You are redirected back and the panel shows *Connected*.
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

### 3. Stream Deck

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
- `music`: `playId` starts a playlist/track/album/artist — a `spotify:` URI or `open.spotify.com` link (a pasted share link works as-is); `volume` is 0–1 (mapped to the Spotify device volume), or `"pause": true` to stop playback.
- `soundEffects`: a list of sound ids (from the Sounds tab) to fire — e.g. `"soundEffects": ["thunder", "rain"]`.
- Any part can be omitted — light, music and sound effects are applied concurrently, and the response reports each part separately (HTTP 207 if something failed).

## Soundboard

Import your own sound effects and fire them from the panel's **Sounds** tab or from a Stream Deck. Audio plays on the **machine running the app** (like the old Kenku FM setup) — so it comes out of the same speakers no matter which panel or button triggered it, including scene activations.

- **Import** — Sounds tab → *Import a sound* → pick an **MP3, WAV or OGG** file. It's uploaded to the server and stored under `%LocalAppData%\RpgSceneMaker\sounds`.
- **Tune** — each sound has a **volume** and a **loop** toggle (one-shot vs. continuous ambience). Tap ✎ on a sound to rename it, set its category, adjust volume/looping, and *Preview*.
- **Play** — tap a sound to play, tap again to stop. Sounds **overlap** (thunder over rain); **Stop all** stops everything.
- **From scenes** — in the scene editor's *Sound Effects* section, pick which sounds a scene fires. Activating the scene stops current playback, then plays the picked sounds with their own volume/loop.

> Sound playback uses NAudio and is **Windows-only** (lighting and Spotify work cross-platform).

## Endpoint reference

| Area | Endpoints |
|---|---|
| Scenes | `GET /scenes`, `GET /scenes/active`, `GET/PUT/DELETE /scenes/{id}`, `GET\|POST /scenes/{id}/activate` |
| Lights | `/lights/on`, `/lights/off`, `/lights/toggle`, `/lights/color?hex=FF8C2A&brightness=80`, `/lights/white?brightness=80&temperature=30`, `/lights/brightness?value=50`, `GET /lights/status` |
| Music (Spotify) | `/music/play?id=…` (a `spotify:` URI / `open.spotify.com` link), `/music/pause`, `/music/resume`, `/music/next`, `/music/previous`, `/music/volume?value=0.5`, `/music/shuffle?value=true`, `/music/repeat?mode=off\|track\|playlist`, `GET /music/playlists`, `GET /music/search?q=…`, `GET /music/state` |
| Sounds (soundboard) | `GET /sounds/list`, `POST /sounds/import` (multipart), `PUT\|DELETE /sounds/{id}`, `/sounds/{id}/play?volume=0.8`, `/sounds/{id}/stop`, `/sounds/stop` (all), `GET /sounds/state` |
| Setup (Tuya) | `GET /setup/scan?seconds=10`, `GET /setup/local-keys?accessId=…&apiSecret=…&deviceId=…&region=eu` |
| Setup (Hue) | `GET /setup/hue/discover`, `GET /setup/hue/register?bridgeIp=…`, `GET /setup/hue/lights` |
| Setup (Spotify) | `GET/PUT /setup/spotify/config`, `GET /setup/spotify/login`, `GET /setup/spotify/callback`, `GET /setup/spotify/devices`, `GET\|POST /setup/spotify/disconnect` |
| Setup (config) | `GET /setup/config`, `PUT /setup/config` — read/update provider + Hue/Tuya settings at runtime (persisted to the database) |

All command endpoints accept GET or POST; parameters go in the query string.

## Persistence

Scenes, lighting settings (provider, Hue, Tuya) and the Spotify connection (Client ID, refresh token, preferred device) live in a SQLite database at `%LocalAppData%\RpgSceneMaker\rpg-scene-maker.db` (override with `Database:Path` in `appsettings.json`). The schema is created/upgraded automatically at startup via EF Core migrations. Note the Spotify token in that file grants control of your Spotify account's playback — treat backups of the database accordingly.

On first run with an empty database, the legacy JSON files are imported once: `scenes.json` (also the starter template on a fresh clone) and `settings.local.json` (the pre-SQLite settings overlay). After that the database is the source of truth — the legacy files are never read again and can be kept as a backup or deleted.

## Configuration ([appsettings.json](src/RpgSceneMaker.Api/appsettings.json))

Deployment-level config only — everything else is managed from the panel and stored in the database.

| Key | Meaning |
|---|---|
| `Urls` | Listen address, default `http://0.0.0.0:5252` so tablets on your Wi-Fi can reach the panel. Change to `http://localhost:5252` to lock it to this PC. |
| `Security:ApiKey` | Optional shared secret. When set, all control endpoints require it (`X-Api-Key` header or `?apiKey=`); the panel asks for it under ⚙. |
| `Database:Path` | SQLite file location, default `%LocalAppData%\RpgSceneMaker\rpg-scene-maker.db`. |
| `Sounds:Path` | Folder for imported sound-effect audio files, default `%LocalAppData%\RpgSceneMaker\sounds`. |

> ⚠️ The API listens on your whole LAN by default so the iPad can reach it. On a home network the worst case is someone toggling your lights, but set `Security:ApiKey` anyway — one line of config, and the panel + Stream Deck both support it. Never expose the port to the internet.
