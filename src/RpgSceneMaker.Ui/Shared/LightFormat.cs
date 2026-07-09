namespace RpgSceneMaker.Ui.Shared;

/// <summary>Small display helpers for light state shown in the editor and settings.</summary>
public static class LightFormat
{
    public static string ProviderLabel(string provider) => provider switch
    {
        "hue" => "Hue",
        "tuya" => "Tuya",
        _ => "?",
    };

    public static string TempWord(int temperature) => temperature < 34 ? "warm" : temperature > 66 ? "cold" : "neutral";

    public static string EffectLabel(string type) => Palette.Effects.FirstOrDefault(e => e.Type == type).Label ?? "";
}
