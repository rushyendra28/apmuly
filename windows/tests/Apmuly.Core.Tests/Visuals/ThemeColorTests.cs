using Apmuly.Core.Visuals;

namespace Apmuly.Core.Tests.Visuals;

public class ThemeColorTests
{
    private static readonly Rgb Red = new(0.85, 0.12, 0.2);
    private static readonly Rgb Teal = new(0.05, 0.7, 0.65);
    private static readonly Rgb Black = new(0.02, 0.02, 0.02);
    private static readonly Rgb Grey = new(0.5, 0.5, 0.52);

    private static Rgb[] Pixels(params (Rgb Color, int Count)[] spec) => spec.SelectMany(s => Enumerable.Repeat(s.Color, s.Count)).ToArray();

    private static double Luminance(string hex)
    {
        var c = Rgb.FromHex(hex);
        static double L(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void FindsDominantVividColoursIgnoringGreys()
    {
        var colors = PaletteExtractor.DominantColors(Pixels((Black, 900), (Grey, 500), (Red, 300), (Teal, 120)));
        Assert.Equal(2, colors.Count);
        Assert.True(colors[0].R > 0.7 && colors[0].G < 0.3);
    }

    [Fact]
    public void GreyscaleArtworkHasNoVividColours() =>
        Assert.Empty(PaletteExtractor.DominantColors(Pixels((Black, 10), (Grey, 10))));

    [Fact]
    public void TakesItsHueFromTheDominantColour()
    {
        var palette = ThemePalette.From(Pixels((Black, 500), (Red, 300), (Teal, 100)));
        var highlight = Rgb.FromHex(palette.Highlight);
        Assert.True(highlight.R > highlight.G && highlight.R > highlight.B);
        Assert.NotEqual(ThemePalette.Fallback, palette);
    }

    public static TheoryData<double, double, double> Colours => new()
    {
        { 0.85, 0.12, 0.2 }, { 0.95, 0.9, 0.1 }, { 0.1, 0.2, 0.9 }, { 0.6, 0.95, 0.6 }, { 0.3, 0.05, 0.1 },
    };

    [Theory]
    [MemberData(nameof(Colours))]
    public void StaysReadable(double r, double g, double b)
    {
        var palette = ThemePalette.From(Pixels((new Rgb(r, g, b), 400)));
        Assert.True(Contrast(palette.Lyric, "#000000") >= 7, $"lyric {palette.Lyric}");
        Assert.True(Contrast("#ffffff", palette.Highlight) >= 3, $"highlight {palette.Highlight}");
        Assert.True(Luminance(palette.Secondary) < Luminance(palette.Lyric));
    }

    [Fact]
    public void GreyscaleArtworkGetsTheFallback()
    {
        Assert.Equal(ThemePalette.Fallback, ThemePalette.From(Pixels((Black, 100), (Grey, 100))));
        Assert.Equal(ThemePalette.Fallback, ThemePalette.From([]));
    }

    [Fact]
    public void HexRoundTrips()
    {
        Assert.Equal("#ff8000", Rgb.FromHex("#ff8000").Hex);
        Assert.Equal("#ff8000", new Rgb(1, 0.5, 0).Hex);
        Assert.Equal("#000000", Rgb.FromHex("not hex").Hex);
    }
}
