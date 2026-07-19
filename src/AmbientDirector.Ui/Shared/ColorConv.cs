using System.Globalization;

namespace AmbientDirector.Ui.Shared;

/// <summary>
/// Client-side colour maths for the swatch / colour picker: a tolerant hex parser plus hex ⇄ RGB ⇄ HSL.
/// The picker keeps its own H/S/L state (so dragging saturation to 0 doesn't lose the hue) and only these
/// helpers ever touch hex strings. Canonical output is lower-case <c>"#rrggbb"</c> to match
/// <see cref="Palette.Moods"/> and the old native colour input, so a slider-picked mood round-trips
/// byte-identically; the server upper-cases on save (see the API's <c>LightValidation.NormalizeHex</c>).
/// </summary>
public static class ColorConv
{
    // Accepts "#RGB" / "#RRGGBB" / "RGB" / "RRGGBB", any case, surrounding whitespace. Null when unparseable.
    public static (int R, int G, int B)? TryParseRgb(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().TrimStart('#');
        if (s.Length == 3 && s.All(Uri.IsHexDigit))
            s = string.Concat(s.Select(c => $"{c}{c}"));
        if (s.Length != 6 || !s.All(Uri.IsHexDigit)) return null;
        return (Byte(s, 0), Byte(s, 2), Byte(s, 4));
    }

    /// <summary>Canonical lower-case "#rrggbb" for a raw hex, or null when it can't be parsed.</summary>
    public static string? Normalize(string? raw) =>
        TryParseRgb(raw) is { } c ? Hex(c.R, c.G, c.B) : null;

    public static string Hex(int r, int g, int b) => $"#{Clamp8(r):x2}{Clamp8(g):x2}{Clamp8(b):x2}";

    // ---- RGB ⇄ HSL. Hue in [0,360), saturation/lightness in [0,100]. ----

    public static (int H, int S, int L) RgbToHsl(int r, int g, int b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
        double l = (max + min) / 2.0;
        double h = 0, s = 0;
        if (max > min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == rd) h = (gd - bd) / d + (gd < bd ? 6 : 0);
            else if (max == gd) h = (bd - rd) / d + 2;
            else h = (rd - gd) / d + 4;
            h /= 6;
        }
        return ((int)Math.Round(h * 360) % 360, (int)Math.Round(s * 100), (int)Math.Round(l * 100));
    }

    public static (int R, int G, int B) HslToRgb(int h, int s, int l)
    {
        double hd = (((h % 360) + 360) % 360) / 360.0;
        double sd = Math.Clamp(s, 0, 100) / 100.0;
        double ld = Math.Clamp(l, 0, 100) / 100.0;
        if (sd == 0)
        {
            var v = Clamp8((int)Math.Round(ld * 255));
            return (v, v, v);
        }
        var q = ld < 0.5 ? ld * (1 + sd) : ld + sd - ld * sd;
        var p = 2 * ld - q;
        return (Chan(p, q, hd + 1.0 / 3.0), Chan(p, q, hd), Chan(p, q, hd - 1.0 / 3.0));
    }

    public static string HslToHex(int h, int s, int l)
    {
        var (r, g, b) = HslToRgb(h, s, l);
        return Hex(r, g, b);
    }

    private static int Byte(string s, int i) =>
        int.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static int Chan(double p, double q, double t) => Clamp8((int)Math.Round(Hue2Rgb(p, q, t) * 255));

    private static double Hue2Rgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    private static int Clamp8(int v) => Math.Clamp(v, 0, 255);
}
