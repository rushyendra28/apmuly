namespace Apmuly.Core.Audio;

/// <summary>In-place iterative radix-2 complex FFT (no dependencies, so it runs and is tested anywhere).</summary>
internal static class Fft
{
    public static void Forward(Span<double> real, Span<double> imaginary)
    {
        var n = real.Length;
        if (n != imaginary.Length || n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two and match.", nameof(real));
        }
        BitReverse(real, imaginary);
        for (var size = 2; size <= n; size <<= 1)
        {
            var half = size / 2;
            var angle = -2 * Math.PI / size;
            for (var start = 0; start < n; start += size)
            {
                for (var k = 0; k < half; k++)
                {
                    var (wr, wi) = (Math.Cos(angle * k), Math.Sin(angle * k));
                    var even = start + k;
                    var odd = even + half;
                    var tr = wr * real[odd] - wi * imaginary[odd];
                    var ti = wr * imaginary[odd] + wi * real[odd];
                    real[odd] = real[even] - tr;
                    imaginary[odd] = imaginary[even] - ti;
                    real[even] += tr;
                    imaginary[even] += ti;
                }
            }
        }
    }

    private static void BitReverse(Span<double> real, Span<double> imaginary)
    {
        var n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }
    }
}
