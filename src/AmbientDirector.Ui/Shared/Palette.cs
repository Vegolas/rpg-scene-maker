namespace AmbientDirector.Ui.Shared;

/// <summary>Shared pickers used across the Lights tab and the scene editor.</summary>
public static class Palette
{
    // The mood colour swatches (hex + i18n key for the accessible name; render via L[NameKey]).
    public static readonly (string Hex, string NameKey)[] Moods =
    [
        ("#ff8c2a", "palette.mood.candlelight"),
        ("#ff1e1e", "palette.mood.combat"),
        ("#3a7a4a", "palette.mood.forest"),
        ("#7a3aff", "palette.mood.arcane"),
        ("#4aa8ff", "palette.mood.ice"),
        ("#ff4a8c", "palette.mood.fey"),
        ("#ffd84a", "palette.mood.treasure"),
        ("#20e0c0", "palette.mood.poison"),
    ];

    // Per-light effect options (type + i18n key for the button label; render via L[LabelKey]). The picker
    // shows a chrome icon beside the label via EffectIcon(type); the label is also used in the light summary.
    public static readonly (string Type, string LabelKey)[] Effects =
    [
        ("none", "palette.effect.none"),
        ("flicker", "palette.effect.flicker"),
        ("glow", "palette.effect.glow"),
        ("storm", "palette.effect.storm"),
        ("drift", "palette.effect.drift"),
        ("custom", "palette.effect.custom"),
    ];

    // Chrome icon name (Shared/Icons.cs key) for an effect type; "none" (and unknowns) show no icon.
    public static string EffectIcon(string type) => type switch
    {
        "flicker" => "fx-flicker",
        "glow" => "fx-glow",
        "storm" => "fx-storm",
        "drift" => "fx-drift",
        "custom" => "fx-custom",
        _ => "",
    };

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
