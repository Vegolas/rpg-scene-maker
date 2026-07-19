namespace AmbientDirector.Ui.Shared;

/// <summary>Small display helpers for light state shown in the editor and settings. The label helpers
/// return i18n keys (not text) — render them through the localizer, e.g. <c>@L[LightFormat.TempWordKey(t)]</c>.</summary>
public static class LightFormat
{
    public static string ProviderLabelKey(string provider) => provider switch
    {
        "hue" => "light.provider.hue",
        "tuya" => "light.provider.tuya",
        _ => "light.provider.unknown",
    };

    public static string TempWordKey(int temperature) =>
        temperature < 34 ? "light.temp.warm" : temperature > 66 ? "light.temp.cold" : "light.temp.neutral";

    public static string EffectLabelKey(string type) => Palette.Effects.FirstOrDefault(e => e.Type == type).LabelKey ?? "";
}
