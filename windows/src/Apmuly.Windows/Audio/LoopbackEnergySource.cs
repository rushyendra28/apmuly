using System.Buffers;
using System.Runtime.InteropServices;
using Apmuly.Core.Audio;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Apmuly.Windows.Audio;

/// <summary>
/// Measures what the PC is playing through WASAPI loopback of the default output device (all apps, not
/// just Apple Music) for beat-reactive visuals. Restarts when the default device changes; on failure the
/// stage simply gets no energy (it pulses on its own) and capture is retried later.
/// </summary>
internal sealed partial class LoopbackEnergySource : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");

    private readonly EnergyMeter _meter = new();
    private readonly ILogger _logger;
    // NAudio 3.x marks WasapiLoopbackCapture obsolete in favour of WasapiRecorder; it still works and is the
    // documented system-wide loopback. A later step could use process loopback to hear only Apple Music.
#pragma warning disable CS0618
    private WasapiLoopbackCapture? _capture;
#pragma warning restore CS0618
    private SampleKind _kind;
    private int _channels;
    private string? _deviceId;
    private volatile bool _running;
    private DateTime _lastCheck = DateTime.MinValue;
    private DateTime _lastFailure = DateTime.MinValue;

    private enum SampleKind
    {
        Float32,
        Pcm16,
        Pcm24,
        Pcm32,
    }

    public LoopbackEnergySource(ILogger<LoopbackEnergySource> logger)
    {
        _logger = logger;
    }

    /// <summary>Energy since the last call, or null when there's no capture or no sound.</summary>
    public EnergySnapshot? Snapshot() => _running ? _meter.Snapshot() : null;

    /// <summary>Starts or restarts capture as needed. Cheap to call every frame (UI thread).</summary>
    public void EnsureRunning()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCheck < CheckInterval)
        {
            return;
        }
        _lastCheck = now;
        var device = DefaultDeviceId();
        if (_running && device == _deviceId)
        {
            return;
        }
        if (!_running && now - _lastFailure < RetryInterval)
        {
            return;
        }
        Stop();
        if (device is not null)
        {
            Start(device);
        }
    }

    private void Start(string deviceId)
    {
        try
        {
#pragma warning disable CS0618 // see _capture
            var capture = new WasapiLoopbackCapture();
#pragma warning restore CS0618
            var format = capture.WaveFormat;
            (_kind, _channels) = (KindOf(format), Math.Max(1, format.Channels));
            _meter.SetSampleRate(format.SampleRate);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnStopped;
            _capture = capture;
            _deviceId = deviceId;
            capture.StartRecording();
            _running = true;
            LogStarted(format.SampleRate, format.Channels, _kind);
        }
        catch (Exception error) when (error is COMException or NotSupportedException or InvalidOperationException
                                          or ArgumentOutOfRangeException or UnauthorizedAccessException)
        {
            LogFailed(error);
            _lastFailure = DateTime.UtcNow;
            Stop();
        }
    }

    private static SampleKind KindOf(WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible ext && ext.SubFormat == FloatSubFormat);
        var isPcm = format.Encoding == WaveFormatEncoding.Pcm
            || (format is WaveFormatExtensible pcm && pcm.SubFormat == PcmSubFormat);
        return (isFloat, isPcm, format.BitsPerSample) switch
        {
            (true, _, 32) => SampleKind.Float32,
            (_, true, 16) => SampleKind.Pcm16,
            (_, true, 24) => SampleKind.Pcm24,
            (_, true, 32) => SampleKind.Pcm32,
            _ => throw new NotSupportedException($"Unsupported loopback format {format.Encoding}/{format.BitsPerSample}"),
        };
    }

    /// <summary>Runs on the capture thread.</summary>
    private void OnData(object? sender, WaveInEventArgs e)
    {
        var bytes = e.Buffer.AsSpan(0, Math.Min(e.BytesRecorded, e.Buffer.Length));
        if (_kind == SampleKind.Float32)
        {
            _meter.AddInterleaved(MemoryMarshal.Cast<byte, float>(bytes), _channels);
            return;
        }
        var width = _kind switch { SampleKind.Pcm16 => 2, SampleKind.Pcm24 => 3, _ => 4 };
        var count = bytes.Length / width;
        var samples = ArrayPool<float>.Shared.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                samples[i] = Decode(bytes.Slice(i * width, width));
            }
            _meter.AddInterleaved(samples.AsSpan(0, count), _channels);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(samples);
        }
    }

    private static float Decode(ReadOnlySpan<byte> s) => s.Length switch
    {
        2 => BitConverter.ToInt16(s) / 32768f,
        3 => ((s[2] << 24) | (s[1] << 16) | (s[0] << 8)) / 2147483648f,
        _ => BitConverter.ToInt32(s) / 2147483648f,
    };

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _running = false;
        if (e.Exception is not null)
        {
            LogFailed(e.Exception);
            _lastFailure = DateTime.UtcNow;
        }
    }

    private static string? DefaultDeviceId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private void Stop()
    {
        _running = false;
        var capture = _capture;
        _capture = null;
        _deviceId = null;
        if (capture is null)
        {
            return;
        }
        capture.DataAvailable -= OnData;
        capture.RecordingStopped -= OnStopped;
        try
        {
            capture.StopRecording();
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ObjectDisposedException)
        {
            LogFailed(error);
        }
        capture.Dispose();
    }

    public void Dispose() => Stop();

    [LoggerMessage(Level = LogLevel.Information, Message = "Audio loopback started ({Rate} Hz, {Channels} ch, {Kind})")]
    private partial void LogStarted(int rate, int channels, SampleKind kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audio loopback unavailable")]
    private partial void LogFailed(Exception error);
}
