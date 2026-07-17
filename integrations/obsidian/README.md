# RPG Scene Maker — Obsidian plugin

Fire [RPG Scene Maker](../../README.md) scenes, events and sounds straight from your session
notes. Write an inline token and it renders as a clickable button — tap it while you read to
switch the table's lights + music, without leaving the note.

```md
The party reaches the gates of Stonebrook. `sm:scene:city`

As they haggle in the market you can drop in a `sm:sound:crowd` or, when the guards
turn hostile, `sm:event:alarm-bell`.
```

## What it does

- **Inline buttons** in your notes from a compact syntax (below). Clicking fires a background
  request to your server (via Obsidian's `requestUrl`, so no CORS issues and no browser tab
  opens) and shows a small toast with the result.
- **Autocomplete**: type `sm:` and it suggests the kinds; type `sm:scene:` and it suggests the
  scenes that actually exist on your server, with their name and tile art.
- **Art & emoji**: a button shows the entity's uploaded tile art (or its leading emoji), pulled
  live from the server.
- Works in **Reading view** and **Live Preview**, on **desktop and mobile** (iPad) Obsidian.

## Syntax

Put the token in an inline code span (single backticks):

| Token | Fires |
| --- | --- |
| `` `sm:scene:<id>` `` | Activate a scene (`GET /scenes/<id>/activate`) |
| `` `sm:event:<id>` `` | Trigger an event (`GET /events/<id>/trigger`) |
| `` `sm:sound:<id>` `` | Play a sound (`GET /sounds/<id>/play`) |
| `` `sm:music:<uri>` `` | Play a Spotify URI/link (`GET /music/play?id=<uri>`) |
| `` `sm:lights:reset` `` | Reset lights to default (also `:off`, `:on`) |

Add a custom button label after a pipe: `` `sm:scene:city|▶ Enter the city` ``.
Without one, the button uses the entity's own name from the server.

## Install

This plugin lives in the app repo but isn't in the Obsidian community store. Build it and drop it
into your vault:

```bash
cd integrations/obsidian
npm install
npm run build          # produces main.js
```

Then copy `main.js`, `manifest.json` and `styles.css` into your vault at
`<vault>/.obsidian/plugins/rpg-scene-maker/`, and enable **RPG Scene Maker** under
*Settings → Community plugins*. (On mobile, copy the same three files into that folder via your
sync of choice.)

For iterating on the plugin, `npm run dev` rebuilds on change; use the
[Hot-Reload](https://github.com/pjeby/hot-reload) plugin to reload it in Obsidian automatically.

## Configure

Open *Settings → RPG Scene Maker* and set:

- **Server address** — your API's base URL, e.g. `http://192.168.1.20:5252` (the same address you
  open the panel at). On the same machine as the server, `http://localhost:5252` works.
- **API key** — only if you set `Security:ApiKey` on the server. It's stored in the vault's plugin
  data, **never** written into your notes, so it isn't exposed when notes sync.
- **Show tile art** — toggle thumbnails on the buttons.

Hit **Test** to confirm the connection. If ids change on the server, run the *Refresh scene /
event / sound lists* command (or just wait — the list cache is short-lived).

## Notes

- The server must be reachable from the device running Obsidian (same LAN). This is the same
  requirement as opening the panel.
- A button whose id isn't found on the server renders with a dashed outline — usually a typo or a
  renamed/deleted entity.
