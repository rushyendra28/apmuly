namespace Apmuly.Core.Visuals;

/// <summary>A song's colours, taken from its album artwork, for the stage themes.</summary>
/// <param name="Lyric">Lyric words (readable on black).</param>
/// <param name="Highlight">The current word's highlight (white text stays readable on it).</param>
/// <param name="Secondary">Accents and background detail (dimmer than the lyric colour).</param>
public sealed record ThemePalette(string Lyric, string Highlight, string Secondary)
{
    /// <summary>Used when artwork is missing or has no vivid colour.</summary>
    public static readonly ThemePalette Fallback = new("#e9e4f2", "#6a58e6", "#4d4863");

    private const double LyricContrastOnBlack = 7.5;
    private const double WhiteContrastOnHighlight = 3.2;
    private static readonly Rgb Black = new(0, 0, 0);
    private static readonly Rgb White = new(1, 1, 1);

    public static ThemePalette From(IEnumerable<Rgb> pixels)
    {
        var colors = PaletteExtractor.DominantColors(pixels, maxColors: 1, minimumColors: 1);
        if (colors.Count == 0)
        {
            return Fallback;
        }
        var hue = colors[0].Hue;
        var saturation = colors[0].Saturation;

        // Highlight: vivid, and dark enough for white text on it.
        var highlightSaturation = Math.Min(0.85, Math.Max(0.45, saturation));
        var highlight = Stride(0.56, 0.2, -0.02)
            .Select(l => Rgb.Hsl(hue, highlightSaturation, l))
            .Cast<Rgb?>()
            .FirstOrDefault(c => c!.Value.Contrast(White) >= WhiteContrastOnHighlight)
            ?? Rgb.Hsl(hue, highlightSaturation, 0.3);

        // Lyric: a soft tint of the same hue, light enough to read on black.
        var lyricSaturation = Math.Min(0.6, saturation) * 0.8;
        var lyric = Stride(0.74, 0.96, 0.02)
            .Select(l => Rgb.Hsl(hue, lyricSaturation, l))
            .Cast<Rgb?>()
            .FirstOrDefault(c => c!.Value.Contrast(Black) >= LyricContrastOnBlack)
            ?? Rgb.Hsl(hue, lyricSaturation, 0.95);

        var secondary = Rgb.Hsl(hue, Math.Min(0.5, saturation) * 0.6, 0.32);
        return new ThemePalette(lyric.Hex, highlight.Hex, secondary.Hex);
    }

    /// <summary>Like Swift's <c>stride(from:through:by:)</c> for doubles (start + i·step, no accumulation).</summary>
    private static IEnumerable<double> Stride(double from, double through, double by)
    {
        for (var i = 0; ; i++)
        {
            var value = from + i * by;
            if (by > 0 ? value > through : value < through)
            {
                yield break;
            }
            yield return value;
        }
    }
}
