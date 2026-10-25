using System.Globalization;

namespace Apmuly.Core.Visuals;

/// <summary>An sRGB colour with 0…1 channels.</summary>
public readonly record struct Rgb(double R, double G, double B)
{
    /// <summary>From "#rrggbb" (invalid input reads as black).</summary>
    public static Rgb FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var digits = hex.Trim('#');
        var value = int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        return new Rgb(((value >> 16) & 0xFF) / 255.0, ((value >> 8) & 0xFF) / 255.0, (value & 0xFF) / 255.0);
    }

    public string Hex => $"#{Channel(R):x2}{Channel(G):x2}{Channel(B):x2}";

    internal double Value => Math.Max(R, Math.Max(G, B));

    public double Saturation
    {
        get
        {
            var max = Value;
            return max == 0 ? 0 : (max - Math.Min(R, Math.Min(G, B))) / max;
        }
    }

    /// <summary>0…1 hue.</summary>
    internal double Hue
    {
        get
        {
            var max = Value;
            var delta = max - Math.Min(R, Math.Min(G, B));
            if (delta <= 0)
            {
                return 0;
            }
            double sector;
            if (max == R)
            {
                sector = ((G - B) / delta) % 6;
            }
            else if (max == G)
            {
                sector = (B - R) / delta + 2;
            }
            else
            {
                sector = (R - G) / delta + 4;
            }
            var h = sector / 6;
            return h < 0 ? h + 1 : h;
        }
    }

    /// <summary>WCAG relative luminance.</summary>
    internal double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

    /// <summary>WCAG contrast ratio with another colour (1…21).</summary>
    internal double Contrast(Rgb other)
    {
        var (a, b) = (Luminance, other.Luminance);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>From hue / saturation / lightness (all 0…1).</summary>
    internal static Rgb Hsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h * 6) % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = ((int)(h * 6) % 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new Rgb(r + m, g + m, b + m);
    }

    private static double Linear(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static int Channel(double v) => (int)Math.Round(Math.Min(1, Math.Max(0, v)) * 255, MidpointRounding.AwayFromZero);
}
