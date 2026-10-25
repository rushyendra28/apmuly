using Apmuly.Core.Text;

namespace Apmuly.Core.Lyrics;

/// <summary>
/// Estimates per-word timing inside a line from how words are usually sung,
/// for sources that only provide line-level timestamps: syllable counts, quick filler words,
/// a longer hold on the line's last word and a short breath at commas.
/// </summary>
public static class WordTimingEstimator
{
    /// <summary>Singers stretch the last word of a phrase.</summary>
    internal const double PhraseFinalHold = 1.6;
    /// <summary>…and pause briefly at a comma.</summary>
    internal const double CommaHold = 1.3;
    /// <summary>One-syllable filler words are sung quicker than content words.</summary>
    internal const double FillerWeight = 0.6;
    /// <summary>Upper bound on how long one syllable is sung; keeps short lines from stretching across long instrumental gaps.</summary>
    internal const double MaxSecondsPerSyllable = 0.45;
    internal const double HeldNoteAllowance = 0.5;

    private static readonly HashSet<string> FillerWords =
    [
        "a", "an", "the", "i", "im", "to", "of", "in", "on", "at", "and", "or", "but", "my", "me", "you", "your",
        "it", "its", "is", "be", "we", "he", "she", "so", "for", "with", "as", "by", "if", "oh", "da", "la", "na",
        "de", "el", "y", "que", "en", "le", "les", "et", "un", "une", "der", "die", "das", "und",
    ];

    private const string Vowels = "aeiouy";

    public static IReadOnlyList<LyricWord> Estimate(string text, double start, double end)
    {
        var tokens = TextFolding.SplitWords(text);
        if (tokens.Length == 0 || end <= start)
        {
            return [];
        }

        var weights = Weights(tokens);
        var totalWeight = weights.Sum();
        var syllableCount = tokens.Sum(token => (double)Syllables(token));
        var span = Math.Min(end - start, syllableCount * MaxSecondsPerSyllable + HeldNoteAllowance);

        var boundaries = new double[tokens.Length + 1];
        boundaries[0] = start;
        for (var i = 0; i < weights.Count; i++)
        {
            boundaries[i + 1] = boundaries[i] + span * weights[i] / totalWeight;
        }
        boundaries[^1] = start + span; // no floating-point drift past the line end
        return tokens.Select((token, i) => new LyricWord(token, boundaries[i], boundaries[i + 1])).ToArray();
    }

    /// <summary>Relative sung length of each word of a line (all &gt; 0).</summary>
    public static IReadOnlyList<double> Weights(IReadOnlyList<string> tokens)
    {
        var weights = new double[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var syllableCount = Syllables(token);
            var bare = TextFolding.Letters(token.ToLowerInvariant());
            var weight = (double)syllableCount;
            if (syllableCount == 1 && FillerWords.Contains(bare))
            {
                weight *= FillerWeight;
            }
            if (i == tokens.Count - 1 && tokens.Count > 1)
            {
                weight *= PhraseFinalHold;
            }
            else if (token.EndsWith(',') || token.EndsWith(';') || token.EndsWith('—'))
            {
                weight *= CommaHold;
            }
            weights[i] = weight;
        }
        return weights;
    }

    /// <summary>Longest plausible sung duration for <paramref name="text"/>, held notes included.</summary>
    internal static double MaxSungDuration(string text)
    {
        var syllableCount = TextFolding.SplitWords(text).Sum(Syllables);
        return Math.Max(1, syllableCount) * MaxSecondsPerSyllable + HeldNoteAllowance;
    }

    /// <summary>Rough syllable count; always ≥ 1 so every word gets some time.</summary>
    public static int Syllables(string word)
    {
        var letters = TextFolding.Letters(TextFolding.FoldCaseAndDiacritics(word));
        if (letters.Length == 0)
        {
            return 1;
        }
        if (!TextFolding.IsAscii(letters))
        {
            // Non-Latin scripts: approximate by character count.
            return Math.Max(1, (TextFolding.RuneCount(letters) + 1) / 2);
        }

        var groups = 0;
        var inVowel = false;
        foreach (var ch in letters)
        {
            var isVowel = Vowels.Contains(ch, StringComparison.Ordinal);
            if (isVowel && !inVowel)
            {
                groups++;
            }
            inVowel = isVowel;
        }
        var silentE = letters.EndsWith('e') && !letters.EndsWith("le", StringComparison.Ordinal) && groups > 1;
        return Math.Max(1, groups - (silentE ? 1 : 0));
    }
}
