using Apmuly.Core.Lyrics;
using Apmuly.Core.Text;

namespace Apmuly.Core.Insights;

/// <summary>
/// Decides which emoji and pictures to show per line: an emotion emoji on every expressive
/// line (with a "burst" when the mood changes), plus objects and actions from the analyzer
/// and from the line's own words.
/// </summary>
public static class InsightPlanner
{
    /// <summary>The same object/action isn't shown again on the very next line.</summary>
    public const int ObjectCooldown = 1;
    public const int MaxObjectsPerLine = 3;
    private const int MaxObjectLength = 24;

    /// <returns>Insights keyed by line index; lines with nothing to show are omitted.</returns>
    public static IReadOnlyDictionary<int, LineInsight> Plan(IReadOnlyList<LyricLine> lines, IReadOnlyDictionary<int, LineReading> readings)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(readings);
        Emotion? lastEmotion = null;
        var lastShown = new Dictionary<string, int>();
        var result = new Dictionary<int, LineInsight>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.IsGap || !readings.TryGetValue(index, out var reading))
            {
                continue;
            }
            var expressive = reading.Emotion != Emotion.Neutral;
            var emoji = expressive ? EmojiLexicon.EmojiFor(reading.Emotion, index) : null;
            var burst = expressive && reading.Emotion != lastEmotion;
            if (expressive)
            {
                lastEmotion = reading.Emotion;
            }

            var cues = PickCues(line, reading, index, lastShown);
            if (emoji is not null || cues.Count > 0)
            {
                result[index] = new LineInsight(emoji, burst, cues);
            }
        }
        return result;
    }

    private static List<ObjectCue> PickCues(LyricLine line, LineReading reading, int index, Dictionary<string, int> lastShown)
    {
        var seen = new HashSet<string>();
        var cues = reading.Objects
            .Select(raw => ModelCue(raw, line))
            .OfType<ObjectCue>()
            .Concat(LineCues(line))
            .ToList()
            .Where(cue => seen.Add(cue.Word))
            .ToList()
            .Where(cue => !lastShown.TryGetValue(cue.Word, out var shown) || index - shown > ObjectCooldown)
            .Take(MaxObjectsPerLine)
            .ToList();
        cues.ForEach(cue => lastShown[cue.Word] = index);
        return cues;
    }

    /// <summary>An object or action named by the analyzer, with its emoji if one fits.</summary>
    private static ObjectCue? ModelCue(string raw, LyricLine line)
    {
        if (ValidObject(raw) is not { } word)
        {
            return null;
        }
        var action = EmojiLexicon.EmojiForAction(word);
        return new ObjectCue(word, action ?? EmojiLexicon.EmojiForObject(word), WordIndexOf(word, line));
    }

    /// <summary>Emoji-able words straight from the line (actions first, then objects).</summary>
    private static IEnumerable<ObjectCue> LineCues(LyricLine line)
    {
        for (var i = 0; i < line.Words.Count; i++)
        {
            var text = line.Words[i].Text;
            if (EmojiLexicon.EmojiForAction(text) is { } action)
            {
                yield return new ObjectCue(EmojiLexicon.Normalize(text), action, i);
            }
            else if (EmojiLexicon.EmojiForObject(text) is { } emoji)
            {
                yield return new ObjectCue(EmojiLexicon.Canonical(text), emoji, i);
            }
        }
    }

    /// <summary>Canonical object word, or null if it's empty, abstract, too long, or not made of letters.</summary>
    private static string? ValidObject(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || TextFolding.RuneCount(trimmed) > MaxObjectLength
            || !trimmed.EnumerateRunes().All(r => System.Text.Rune.IsLetter(r) || r.Value == ' ' || r.Value == '-'))
        {
            return null;
        }
        var word = EmojiLexicon.Canonical(trimmed);
        return word.Length == 0 || EmojiLexicon.IsAbstract(word) ? null : word;
    }

    private static int? WordIndexOf(string word, LyricLine line)
    {
        for (var i = 0; i < line.Words.Count; i++)
        {
            var text = line.Words[i].Text;
            if (EmojiLexicon.Canonical(text) == word || EmojiLexicon.Normalize(text) == word)
            {
                return i;
            }
        }
        return null;
    }
}
