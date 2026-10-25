using Apmuly.Core.Lyrics;

namespace Apmuly.Core.Insights;

/// <summary>
/// Windows' line analyzer: the Mac app's fallback path without NaturalLanguage. Emotion comes from
/// keyword cues (no sentiment model, so sentiment is neutral); objects come from the planner's own
/// lexicon pass over the line's words. Lyric text never leaves the PC and is never logged.
/// </summary>
public static class LexiconAnalyzer
{
    public static LineReading Reading(LyricLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var emotion = EmotionLexicon.EmotionFor(line.Words.Select(w => w.Text), sentiment: 0);
        return new LineReading(emotion, []);
    }

    /// <summary>Readings for every sung line, then the planned insights.</summary>
    public static IReadOnlyDictionary<int, LineInsight> Insights(LyricsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var readings = new Dictionary<int, LineReading>();
        for (var i = 0; i < document.Lines.Count; i++)
        {
            if (!document.Lines[i].IsGap)
            {
                readings[i] = Reading(document.Lines[i]);
            }
        }
        return InsightPlanner.Plan(document.Lines, readings);
    }
}
