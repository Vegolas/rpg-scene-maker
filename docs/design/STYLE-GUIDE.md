# Scene Maker — "Control Room" Design System

A dark, high-contrast control-panel aesthetic for a tabletop-RPG music & light
manager. The app sits on a tablet at the DM's elbow during play: it must be
**readable at arm's length in a dim room** and operable with a glance and one
tap. Flashy comes from bold color and confident type — never from decoration.

Files:
- `tokens.css` — CSS custom properties + reference component classes. Import it
  (or copy the variables into your styling solution) and use tokens everywhere.
  Never hardcode a hex that exists as a token.
- Live references: `Design System.dc.html` (components), `Reference Screens.dc.html`
  (Scenes screen + Effect editor, tablet landscape 4:3) — in the
  [Claude Design project](https://claude.ai/design/p/0990db45-b305-49b6-8c07-3542fb587502).

## Core principles

1. **Solid surfaces only.** No glassmorphism, no blur, no translucent panels,
   no gradient backgrounds. Depth = surface steps (`--bg` → `--surface` →
   `--card`) + 1px borders. Shadows are not used for elevation.
2. **One accent.** Electric blue `--accent` marks *selection and primary
   action* — active nav item, running scene, primary button, keyframes,
   slider fills. If everything is blue, nothing is. Semantic colors
   (green/red/yellow) only for status.
3. **Scene colors are content.** The vivid palette (`--scene-*`) belongs to
   user content: scene tiles, mood swatches, light previews. UI chrome never
   uses them.
4. **Big targets, instant reads.** Everything tappable ≥ 44px (`--hit-min`).
   List rows ≥ 48px. Primary state (what's playing / running) always visible
   in a status strip at the top of the content area.
5. **Bold-forward type.** UI labels 600–800 weight. Small text gets wide
   tracking + uppercase (`--text-label`, `--text-tag`), never lighter weight.
6. **Glow is semantic.** The only glows allowed: the online status dot and the
   live light-preview bulb. Nothing else glows.

## Layout (tablet landscape, 4:3)

```
┌──────┬──────────────────────────────────────┐
│ rail │ topbar (64px): title · search · play │
│ 96px │──────────────────────────────────────│
│      │ status strip (panel--active) if      │
│ nav  │ something is running                 │
│ items│──────────────────────────────────────│
│ 56px │ section header (uppercase label)     │
│ each │ content: grid of tiles / 2-col rows  │
└──────┴──────────────────────────────────────┘
```

- Left **nav rail** (`.rail`), not a bottom bar. Active item = `--accent-soft`
  fill + `--accent-text` label; inactive icons at 50% opacity.
- **Top bar** (`.topbar`): screen title, search input (flex), ONLINE badge,
  primary play button, house-lights button, settings.
- **Status strip** (`.panel--active`): blue border + 3px inset left bar. Shows
  NOW RUNNING label (`--accent-text`, uppercase), name in `--text-xl`, context
  chips (current playlist, light state), and a Stop button.
- Content grids: scene tiles 3-up, list rows 2-up. Gap `--s-3`/`--s-4`.
- Page padding `--s-4`.

## Color rules

| Use | Token |
|---|---|
| App background | `--bg` |
| Bars, rails, panels | `--surface` |
| Buttons, rows, cards | `--card` (hover `--card-raised`) |
| Recessed wells, hint bars | `--well` |
| Inputs, slider tracks | `--input` |
| Selected surface | `--accent-soft` + `--accent-border` |
| Primary action | `--accent`, white text |
| Running/hot state | `--live` (edge vignette + 2px border on tiles, LIVE badge text on black) |
| Connected / OK | `--success` family |
| Destructive | `--danger` family (soft bg + border + colored text — never solid red buttons) |

Text on colored scene tiles: white with `text-shadow: 0 1px 0 rgba(0,0,0,.25)`
if needed; black on `--scene-amber`/`--scene-yellow`/`--live`.

## Type scale

| Token | Use |
|---|---|
| `--text-2xl` 24/800 | Page heading ("Scenes") |
| `--text-xl` 18/800 | Status strip name, tile names |
| `--text-lg` 15/700 | Row titles, app name |
| `--text-md` 13/700 | Buttons, inputs |
| `--text-sm` 12/600 | Meta ("6 saved", chip text) |
| `--text-label` 11/700 +1.5px caps | Section headers |
| `--text-tag` 10/800 +1px caps | Badges (LIVE, ONLINE, USED IN 3 SCENES) |

Font: Inter (fallback system-ui). No serif, no display fonts.

## Components (see tokens.css for exact styles)

- **Buttons** `.btn` — default (card-colored), `--primary` (blue), `--danger`
  (soft red), `--ghost`, `--icon` (44px square). Hover = lighter surface +
  stronger border; press = 1px translateY; focus = 2px accent outline.
- **Rows** `.row` — icon · title (flex) · meta · chevron.
- **Tiles** `.tile` — icon top-left, badge top-right, name bottom-left.
  Neutral card by default; `--colored` for mood-colored; `--live` for running.
  Tiles can carry user-uploaded background art, so `--live` is NOT a fill:
  it's a 2px `--live` border + radial edge vignette (solid red at edges,
  aggressively transparent by the tile's middle) so the art stays readable.
  "New scene" = `.card--new` dashed.
- **Inputs** `.input`, **search** = input with `⌕` prefix.
- **Toggle** `.toggle`, **slider** `.slider` (24px thumb, white with blue
  ring), **segmented** `.seg` for 2–3 mutually-exclusive options.
- **Badges** `.badge--online`, `.badge--live`; **chips** `.chip` for context
  info (playlist name, light state).
- **Keyframe editor**: `.track` rows (Color = gradient strip preview, the ONE
  place a gradient is allowed, because it previews light animation;
  Brightness = positioned dots), `.key` dots 14px white-ringed, selected key
  gets outer blue ring; per-key panel below with swatch row + Ease segmented +
  Delete key (danger).

## Motion

- `--t-fast` 120ms for hover/press; `--t-med` 200ms for selection/panel moves.
- Allowed flourishes: status-dot pulse (2s), live-preview bulb flicker,
  press scale .985 on tiles. Nothing else animates ambiently.
- No parallax, no springs, no page transitions over 250ms.

## Iconography

UI chrome uses a single-weight **solid icon set** — Phosphor **Fill** — rendered
as inline SVG by `Components/Icon.razor` (path data in `Shared/Icons.cs`, keyed by
a semantic name like `edit` / `play` / `warn`). Icons are tinted `currentColor` and
sized `1em`, so each slot's font-size sets the glyph: ~20–22px in rails/rows,
24–26px in tiles. Inactive nav icons get `opacity: .5`. Add a glyph by dropping its
Phosphor Fill inner markup into `Icons.cs` under a new semantic key — never inline
raw emoji in chrome.

**Emoji are content, not chrome.** The emoji a user picks for a scene/event/screen
name (from `Shared/Palette.cs` → `Emojis`) stay as emoji; a tile or row with no
picked emoji falls back to its section's icon via `Components/Glyph.razor`. The
only iOS side-bearing nudge left applies to the emoji **picker** cells.

## Voice

Labels are short and imperative: "New scene", "Test on lights", "Save effect",
"Stop". No lorem, no exclamation marks. Uppercase only via the label/tag
styles.
