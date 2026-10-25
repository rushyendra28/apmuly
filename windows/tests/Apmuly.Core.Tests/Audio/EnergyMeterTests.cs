using Apmuly.Core.Audio;

namespace Apmuly.Core.Tests.Audio;

public class EnergyMeterTests
{
    private static float[] Sine(double hz, double amplitude, int frames, int channels = 2, double rate = 48_000)
    {
        var samples = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var v = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / rate));
            for (var c = 0; c < channels; c++)
            {
                samples[i * channels + c] = v;
            }
        }
        return samples;
    }

    [Fact]
    public void NoAudioMeansNoSnapshot() => Assert.Null(new EnergyMeter().Snapshot());

    [Fact]
    public void SequenceIncrementsAndAccumulatorsReset()
    {
        var meter = new EnergyMeter();
        meter.AddInterleaved(Sine(440, 0.5, 4096), 2);
        Assert.Equal(1, meter.Snapshot()!.Sequence);
        Assert.Null(meter.Snapshot());
        meter.AddInterleaved(Sine(440, 0.5, 4096), 2);
        Assert.Equal(2, meter.Snapshot()!.Sequence);
    }

    [Fact]
    public void LevelIsRms()
    {
        var meter = new EnergyMeter();
        meter.AddInterleaved(Sine(1000, 1.0, 48_000), 2);
        Assert.Equal(Math.Sqrt(0.5), meter.Snapshot()!.Level, 2);
    }

    [Fact]
    public void BassFollowsLowFrequenciesOnly()
    {
        var low = new EnergyMeter();
        low.AddInterleaved(Sine(60, 0.8, 48_000), 2);
        var high = new EnergyMeter();
        high.AddInterleaved(Sine(5000, 0.8, 48_000), 2);
        Assert.True(low.Snapshot()!.Bass > 0.4);
        Assert.True(high.Snapshot()!.Bass < 0.05);
    }

    [Fact]
    public void SpectrumPeaksInTheRightBand()
    {
        var meter = new EnergyMeter();
        meter.AddInterleaved(Sine(1000, 0.5, 4096), 2);
        var bands = meter.Snapshot()!.Bands;
        Assert.Equal(EnergyMeter.BandCount, bands.Count);
        Assert.All(bands, b => Assert.InRange(b, 0, 1));
        var loudest = bands.Select((b, i) => (b, i)).MaxBy(x => x.b).i;
        // Bands are log-spaced from 40 Hz by (12000/40)^(1/24): 1 kHz falls in band 13.
        Assert.Equal(13, loudest);
        Assert.True(bands[13] > 0.8);
        Assert.True(bands[2] < 0.3);
    }

    [Fact]
    public void ScaleMatchesTheMacAnalysis()
    {
        // Mac scaling: vDSP real FFT (×2), Hann coherent gain 0.5, ÷N → a bin-centred sine of amplitude A peaks at A/2.
        // A = 0.01 → 20·log10(0.005) ≈ -46.0 dB → (−46.0 + 72) / 60 ≈ 0.433.
        var bands = EnergyMeter.Spectrum(Sine(48_000.0 / 2048 * 43, 0.01, 2048, 1), 48_000);
        Assert.Equal(0.433, bands.Max(), 2);
    }

    [Fact]
    public void RejectsBadInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyMeter(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyMeter().AddInterleaved(new float[4], 0));
        var meter = new EnergyMeter();
        meter.AddInterleaved([float.NaN, float.PositiveInfinity], 2);
        Assert.Equal(0, meter.Snapshot()!.Level);
    }
}
