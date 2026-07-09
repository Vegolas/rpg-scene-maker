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
    ];

    // Emoji choices for the scene name picker.
    public static readonly string[] Emojis =
        ["🍺", "🔥", "🌲", "⚔️", "🕯️", "🌊", "👻", "💰", "🎻", "🌙", "⛺", "🐉", "❄️", "🏰", "🎲"];
}
