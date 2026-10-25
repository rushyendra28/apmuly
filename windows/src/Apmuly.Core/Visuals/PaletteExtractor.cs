namespace Apmuly.Core.Visuals;

/// <summary>Picks a few vivid, distinct colours from album artwork pixels.</summary>
public static class PaletteExtractor
{
    private const double MinValue = 0.15;
    private const double MinSaturation = 0.25;
    private const double MinHueDistance = 0.06;
    private const double Levels = 8.0;

    /// <summary>Most prominent vivid colours first. Returns [] when fewer than <paramref name="minimumColors"/> are found.</summary>
    public static IReadOnlyList<Rgb> DominantColors(IEnumerable<Rgb> pixels, int maxColors = 4, int minimumColors = 2)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var swatches = pixels
            .Where(IsVivid)
            .GroupBy(BucketKey)
            .Select(bucket =>
            {
                var members = bucket.ToArray();
                double n = members.Length;
                var mean = new Rgb(members.Sum(c => c.R) / n, members.Sum(c => c.G) / n, members.Sum(c => c.B) / n);
                return (Color: mean, Score: n * (0.3 + mean.Saturation));
            })
            .OrderByDescending(swatch => swatch.Score)
            .Select(swatch => swatch.Color);

        var picked = new List<Rgb>();
        foreach (var color in swatches)
        {
            if (picked.Count < maxColors && picked.All(p => HueDistance(p.Hue, color.Hue) >= MinHueDistance))
            {
                picked.Add(color);
            }
        }
        return picked.Count >= minimumColors ? picked : [];
    }

    private static bool IsVivid(Rgb c) => c.Value >= MinValue && c.Saturation >= MinSaturation;

    private static int BucketKey(Rgb c)
    {
        static int Q(double v) => (int)Math.Min(Levels - 1, Math.Floor(v * Levels));
        return Q(c.R) * 64 + Q(c.G) * 8 + Q(c.B);
    }

    private static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b);
        return Math.Min(d, 1 - d);
    }
}
