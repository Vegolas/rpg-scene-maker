namespace RpgSceneMaker.Ui.Shared;

/// <summary>Shared pickers used across the Lights tab and the scene editor.</summary>
public static class Palette
{
    // The mood colour swatches (hex + accessible name).
    public static readonly (string Hex, string Name)[] Moods =
    [
        ("#ff8c2a", "Candlelight"),
        ("#ff1e1e", "Blood / combat"),
        ("#3a7a4a", "Forest / dungeon"),
        ("#7a3aff", "Arcane"),
        ("#4aa8ff", "Ice / water"),
        ("#ff4a8c", "Fey"),
        ("#ffd84a", "Treasure"),
        ("#20e0c0", "Poison / ghost"),
    ];

    // Per-light effect options (type + button label).
    public static readonly (string Type, string Label)[] Effects =
    [
        ("none", "None"),
        ("flicker", "🕯️ Flicker"),
        ("glow", "✨ Glow"),
        ("storm", "⛈️ Storm"),
        ("drift", "🌀 Drift"),
        ("custom", "🎬 Custom"),
    ];

    // Which effects actually consume the light's base state, so the editor shows the base pickers only when
    // they do something (mirrors EffectEngine on the server):
    //   • base COLOUR/WHITE is the foundation the effect animates from — static "none" plus flicker/glow/storm;
    //   • base BRIGHTNESS is read by everything except the keyframe-driven custom/fx (drift cycles its own
    //     palette but is still scaled to the base brightness).
    public static bool EffectUsesBaseColor(string type) => type is "none" or "flicker" or "glow" or "storm";
    public static bool EffectUsesBaseBrightness(string type) => type is not ("custom" or "fx");

    // Emoji choices for the scene name picker.
    public static readonly string[] Emojis =
        ["🍺", "🔥", "🌲", "⚔️", "🕯️", "🌊", "👻", "💰", "🎻", "🌙", "⛺", "🐉", "❄️", "🏰", "🎲"];
}
