namespace Apmuly.Core.Media;

/// <summary>One media session as the selector sees it.</summary>
/// <param name="LastActive">Host time it last started playing or changed track (see <see cref="SessionActivity"/>).</param>
public sealed record SessionState(string AppId, MediaSource Source, bool IsPlaying, double LastActive);

/// <summary>
/// Chooses the session to follow. Manual preference: always that player (or nothing if it isn't running).
/// Auto: among Apple Music and Spotify sessions, the one playing (most recently started / changed track if
/// several); if none plays, keep the previously followed player while it exists, else the most recently
/// active one. With no known player at all, the system's current session is followed.
/// </summary>
public static class SourceSelector
{
    public static int? Pick(IReadOnlyList<SessionState> sessions, SourcePreference preference,
                            MediaSource? previous, string? currentAppId)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (preference.Pinned() is { } pinned)
        {
            return FirstIndex(sessions, s => s.Source == pinned);
        }
        var known = Enumerable.Range(0, sessions.Count).Where(i => sessions[i].Source != MediaSource.Other).ToArray();
        if (known.Length == 0)
        {
            return FirstIndex(sessions, s => currentAppId is not null && s.AppId == currentAppId);
        }
        var playing = known.Where(i => sessions[i].IsPlaying).ToArray();
        if (playing.Length > 0)
        {
            return MostRecent(sessions, playing);
        }
        if (previous is { } prior && known.FirstOrDefault(i => sessions[i].Source == prior, -1) is var kept and >= 0)
        {
            return kept;
        }
        return MostRecent(sessions, known);
    }

    private static int MostRecent(IReadOnlyList<SessionState> sessions, int[] indices)
    {
        var best = indices[0];
        foreach (var i in indices.Skip(1))
        {
            if (sessions[i].LastActive > sessions[best].LastActive)
            {
                best = i;
            }
        }
        return best;
    }

    private static int? FirstIndex(IReadOnlyList<SessionState> sessions, Func<SessionState, bool> match)
    {
        for (var i = 0; i < sessions.Count; i++)
        {
            if (match(sessions[i]))
            {
                return i;
            }
        }
        return null;
    }
}

/// <summary>Remembers when each session last started playing or changed track. Not thread-safe.</summary>
public sealed class SessionActivity
{
    private readonly Dictionary<string, (bool Playing, string? Track, double LastActive)> _seen = [];

    /// <param name="trackKey">The session's current track (null when not read this time: unchanged).</param>
    public SessionState Observe(string appId, bool isPlaying, string? trackKey, double hostTime)
    {
        ArgumentNullException.ThrowIfNull(appId);
        var lastActive = double.NegativeInfinity;
        string? track = trackKey;
        if (_seen.TryGetValue(appId, out var prior))
        {
            lastActive = prior.LastActive;
            track = trackKey ?? prior.Track;
            var started = isPlaying && !prior.Playing;
            var changedTrack = trackKey is not null && prior.Track is not null && trackKey != prior.Track;
            if (started || changedTrack)
            {
                lastActive = hostTime;
            }
        }
        else if (isPlaying)
        {
            lastActive = hostTime;
        }
        _seen[appId] = (isPlaying, track, lastActive);
        return new SessionState(appId, MediaSources.Classify(appId), isPlaying, lastActive);
    }

    /// <summary>Forgets sessions that have closed.</summary>
    public void Retain(IEnumerable<string> appIds)
    {
        var keep = appIds.ToHashSet();
        foreach (var gone in _seen.Keys.Where(k => !keep.Contains(k)).ToArray())
        {
            _seen.Remove(gone);
        }
    }
}
