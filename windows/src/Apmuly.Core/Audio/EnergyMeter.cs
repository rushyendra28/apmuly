namespace Apmuly.Core.Audio;

/// <summary>Audio energy since the previous snapshot, for beat-reactive visuals.</summary>
/// <param name="Bass">RMS of the low-passed (≈ &lt;150 Hz) signal — drives beat detection.</param>
/// <param name="Level">Overall RMS.</param>
/// <param name="Bands">Log-spaced spectrum bands, 0…1.</param>
public sealed record EnergySnapshot(int Sequence, double Bass, double Level, IReadOnlyList<double> Bands);

/// <summary>
/// Measures loudness, bass energy and a 24-band spectrum from interleaved float samples — a port of the
/// Mac app's MusicAudioTap analysis. Thread-safe: audio threads add samples, the UI thread takes snapshots.
/// </summary>
public sealed class EnergyMeter
{
    public const double BassCutoffHz = 150.0;
    internal const int FftSize = 2048;
    public const int BandCount = 24;
    private const double LowestHz = 40.0;
    private const double HighestHz = 12_000.0;
    /// <summary>Display range for band levels (dB relative to full scale).</summary>
    private const double FloorDb = -72.0;
    private const double CeilingDb = -12.0;
    /// <summary>The Mac analysis uses vDSP's real FFT, whose output is twice the textbook DFT.</summary>
    private const double VdspScale = 2.0;

    private static readonly double[] Window = HannWindow(FftSize);

    private readonly Lock _gate = new();
    private readonly float[] _ring = new float[FftSize];
    private int _ringIndex;
    private double _sumSquares;
    private double _bassSquares;
    private long _frames;
    private int _sequence;
    private double _lowpass;
    private double _alpha;
    private double _sampleRate;

    public EnergyMeter(double sampleRate = 48_000)
    {
        SetSampleRate(sampleRate);
    }

    public void SetSampleRate(double sampleRate)
    {
        if (!double.IsFinite(sampleRate) || sampleRate < 8_000 || sampleRate > 768_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Unsupported sample rate");
        }
        lock (_gate)
        {
            _sampleRate = sampleRate;
            _alpha = 1 - Math.Exp(-2 * Math.PI * BassCutoffHz / sampleRate);
        }
    }

    /// <summary>Adds interleaved samples (any channel count; mixed to mono).</summary>
    public void AddInterleaved(ReadOnlySpan<float> samples, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        lock (_gate)
        {
            for (var frame = 0; frame + channels <= samples.Length; frame += channels)
            {
                var mono = 0.0;
                for (var c = 0; c < channels; c++)
                {
                    var s = samples[frame + c];
                    mono += float.IsFinite(s) ? s : 0;
                }
                mono /= channels;
                _lowpass += _alpha * (mono - _lowpass);
                _sumSquares += mono * mono;
                _bassSquares += _lowpass * _lowpass;
                _frames++;
                _ring[_ringIndex] = (float)mono;
                _ringIndex = (_ringIndex + 1) & (FftSize - 1);
            }
        }
    }

    /// <summary>Energy since the previous call, or null if no audio arrived.</summary>
    public EnergySnapshot? Snapshot()
    {
        float[] latest;
        double rate;
        int sequence;
        double bass, level;
        lock (_gate)
        {
            if (_frames == 0)
            {
                return null;
            }
            double n = _frames;
            sequence = ++_sequence;
            bass = Math.Sqrt(_bassSquares / n);
            level = Math.Sqrt(_sumSquares / n);
            _sumSquares = 0;
            _bassSquares = 0;
            _frames = 0;
            latest = new float[FftSize];
            Array.Copy(_ring, _ringIndex, latest, 0, FftSize - _ringIndex);
            Array.Copy(_ring, 0, latest, FftSize - _ringIndex, _ringIndex);
            rate = _sampleRate;
        }
        return new EnergySnapshot(sequence, bass, level, Spectrum(latest, rate));
    }

    /// <summary>Log-spaced band levels (0…1) of the latest window.</summary>
    internal static double[] Spectrum(float[] samples, double sampleRate)
    {
        var half = FftSize / 2;
        var real = new double[FftSize];
        var imaginary = new double[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            real[i] = samples[i] * Window[i];
        }
        Fft.Forward(real, imaginary);
        var magnitudes = new double[half];
        for (var k = 0; k < half; k++)
        {
            magnitudes[k] = VdspScale * Math.Sqrt(real[k] * real[k] + imaginary[k] * imaginary[k]);
        }

        var binHz = sampleRate / FftSize;
        var ratio = Math.Pow(HighestHz / LowestHz, 1.0 / BandCount);
        var bands = new double[BandCount];
        for (var band = 0; band < BandCount; band++)
        {
            var low = LowestHz * Math.Pow(ratio, band);
            var lowBin = Math.Max(1, (int)(low / binHz));
            var highBin = Math.Min(half - 1, Math.Max(lowBin + 1, (int)(low * ratio / binHz)));
            var peak = 0.0;
            for (var k = lowBin; k < highBin; k++)
            {
                peak = Math.Max(peak, magnitudes[k]);
            }
            var db = 20 * Math.Log10(Math.Max(1e-9, peak / FftSize));
            bands[band] = Math.Min(1, Math.Max(0, (db - FloorDb) / (CeilingDb - FloorDb)));
        }
        return bands;
    }

    /// <summary>Hann window with a peak of 1 (vDSP's <c>hanningDenormalized</c>).</summary>
    private static double[] HannWindow(int size) =>
        Enumerable.Range(0, size).Select(n => 0.5 * (1 - Math.Cos(2 * Math.PI * n / size))).ToArray();
}
