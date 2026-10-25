using System.Text.Json;
using Apmuly.Core.Provider;

namespace Apmuly.Core.Stage;

/// <summary>
/// The user's per-song lyric nudge ("lyrics later" is positive), persisted in
/// <c>%LOCALAPPDATA%\Apmuly\offsets.json</c>. Thread-safe.
/// </summary>
public sealed class OffsetStore
{
    /// <summary>Nudges beyond this are corrupt data, not a timing preference.</summary>
    public const double MaxMagnitude = 600;
    private const long MaxFileBytes = 4 * 1024 * 1024;

    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, double>? _offsets;

    public OffsetStore(string? path = null)
    {
        _path = path ?? ApmulyPaths.Offsets;
    }

    public double Get(string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        lock (_gate)
        {
            return Loaded().GetValueOrDefault(trackId);
        }
    }

    /// <summary>Stores the nudge rounded to milliseconds; 0 removes it.</summary>
    /// <returns>The stored value and whether it was persisted.</returns>
    public (double Value, bool Saved) Set(string trackId, double seconds)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        var rounded = double.IsFinite(seconds)
            ? Math.Clamp(Math.Round(seconds * 1000, MidpointRounding.AwayFromZero) / 1000, -MaxMagnitude, MaxMagnitude)
            : 0;
        lock (_gate)
        {
            var offsets = Loaded();
            if (rounded == 0)
            {
                offsets.Remove(trackId);
            }
            else
            {
                offsets[trackId] = rounded;
            }
            return (rounded, Persist(offsets));
        }
    }

    private Dictionary<string, double> Loaded() => _offsets ??= Read();

    private Dictionary<string, double> Read()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                return [];
            }
            var raw = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllBytes(_path)) ?? [];
            return raw.Where(kv => kv.Key.Length > 0 && double.IsFinite(kv.Value) && Math.Abs(kv.Value) <= MaxMagnitude)
                      .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private bool Persist(Dictionary<string, double> offsets)
    {
        try
        {
            FileNames.WriteAtomically(_path, JsonSerializer.SerializeToUtf8Bytes(offsets));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
