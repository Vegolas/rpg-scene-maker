using System.Globalization;

namespace AmbientDirector.Api.Services;

/// <summary>Color conversions shared by the light providers.</summary>
public static class ColorMath
{
    public static (int r, int g, int b) ParseHexColor(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException($"'{hex}' is not a valid hex color. Use the RRGGBB format, e.g. FF8C2A.");
        return ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    /// <summary>h in degrees (0-360), s and v in 0-1.</summary>
    public static (double h, double s, double v) RgbToHsv(int r, int g, int b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd) h = 60 * ((bd - rd) / delta + 2);
            else h = 60 * ((rd - gd) / delta + 4);
            if (h < 0) h += 360;
        }
        return (h, max == 0 ? 0 : delta / max, max);
    }

    public static (int r, int g, int b) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        var (rd, gd, bd) = ((int)(h / 60) % 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return ((int)Math.Round((rd + m) * 255), (int)Math.Round((gd + m) * 255), (int)Math.Round((bd + m) * 255));
    }
}
