using System.Text.Json;
using Apmuly.Core.Provider;

namespace Apmuly.Core.Sync;

/// <summary>
/// Remembers learned word timings per track, so the next play is on the singer's pace from the
/// first line. Stored as small JSON files (word positions and times only, no lyric text). Thread-safe.
/// </summary>
public sealed class TimingStore
{
    /// <summary>More observations than any song could produce; larger files are rejected as corrupt.</summary>
    private const int MaxObservations = 20_000;

    private readonly string _directory;
    private readonly Lock _gate = new();

    public TimingStore(string? directory = null)
    {
        _directory = directory ?? ApmulyPaths.Timing;
    }

    public IReadOnlyList<WordObservation> Load(string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        lock (_gate)
        {
            return LoadUnlocked(trackId);
        }
    }

    /// <summary>Merges with what's stored; a newer observation of the same word replaces the older one.</summary>
    /// <exception cref="LyricsException">With <see cref="LyricsErrorKind.Storage"/> when the file can't be written.</exception>
    public void Save(IEnumerable<WordObservation> observations, string trackId)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(trackId);
        lock (_gate)
        {
            var merged = new Dictionary<(int, int), WordObservation>();
            foreach (var observation in LoadUnlocked(trackId).Concat(observations))
            {
                merged[(observation.Line, observation.Word)] = observation;
            }
            var sorted = merged.Values.OrderBy(o => o.Line).ThenBy(o => o.Word).ToArray();
            try
            {
                FileNames.WriteAtomically(FilePath(trackId), JsonSerializer.SerializeToUtf8Bytes(sorted));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new LyricsException(LyricsErrorKind.Storage, inner: error);
            }
        }
    }

    private List<WordObservation> LoadUnlocked(string trackId)
    {
        try
        {
            var path = FilePath(trackId);
            if (!File.Exists(path))
            {
                return [];
            }
            var loaded = JsonSerializer.Deserialize<List<WordObservation>>(File.ReadAllBytes(path)) ?? [];
            return loaded.Count > MaxObservations
                ? []
                : loaded.Where(o => o is not null && o.Line >= 0 && o.Word >= 0 && double.IsFinite(o.Time)).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private string FilePath(string trackId) => Path.Combine(_directory, FileNames.Hashed(trackId, ".json"));
}
