import type SceneMakerPlugin from "./main";
import { DEFAULT_ICON, ListKindOf, SmToken, lightsLabel, splitName } from "./tokens";

/**
 * Build the clickable chip element for a token. Renders immediately with a best-guess
 * label (the token arg), then enhances asynchronously with the entity's real name,
 * leading emoji and — when enabled — its tile art.
 */
export function buildChip(plugin: SceneMakerPlugin, token: SmToken): HTMLElement {
  const el = createEl("a", { cls: ["sm-chip", `sm-chip-${token.kind}`] });
  el.setAttribute("role", "button");
  el.setAttribute("aria-label", `RPG Scene Maker: ${token.kind} ${token.arg}`.trim());
  el.tabIndex = 0;

  const icon = el.createSpan({ cls: "sm-chip-icon", text: DEFAULT_ICON[token.kind] });
  const label = el.createSpan({ cls: "sm-chip-label", text: token.label ?? token.arg ?? token.kind });

  void enhance(plugin, token, el, icon, label);

  const fire = (ev: Event) => {
    ev.preventDefault();
    ev.stopPropagation();
    void plugin.fire(token);
  };
  el.addEventListener("click", fire);
  el.addEventListener("keydown", (ev: KeyboardEvent) => {
    if (ev.key === "Enter" || ev.key === " ") fire(ev);
  });

  return el;
}

async function enhance(
  plugin: SceneMakerPlugin,
  token: SmToken,
  chip: HTMLElement,
  icon: HTMLElement,
  label: HTMLElement,
): Promise<void> {
  const listKind = ListKindOf(token.kind);
  if (!listKind) {
    // music / lights: no entity to resolve — give a sensible default label if none was set.
    if (!token.label) label.setText(token.kind === "music" ? "Play music" : lightsLabel(token.arg));
    return;
  }

  let items;
  try {
    items = await plugin.api.list(listKind);
  } catch {
    return;
  }
  const found = items.find((i) => i.id.toLowerCase() === token.arg.toLowerCase());
  if (!found) {
    chip.addClass("sm-chip-missing");
    chip.setAttribute("aria-label", `RPG Scene Maker: unknown ${token.kind} "${token.arg}"`);
    return;
  }

  const { emoji, label: name } = splitName(found.name);
  if (!token.label) label.setText(name || found.id);

  if (plugin.settings.showThumbnails && found.image) {
    const img = createEl("img", { cls: "sm-chip-art" });
    img.src = plugin.api.imageUrl(found.image);
    img.onerror = () => {
      // Fall back to the emoji / kind glyph if the art can't load.
      icon.setText(emoji || DEFAULT_ICON[token.kind]);
    };
    icon.empty();
    icon.appendChild(img);
  } else if (emoji) {
    icon.setText(emoji);
  }
}
