namespace Apmuly.Core.Insights;

/// <summary>Fallback emotion reading (no on-device model): keyword cues first, then sentiment.</summary>
public static class EmotionLexicon
{
    private static readonly (Emotion Emotion, HashSet<string> Words)[] Cues =
    [
        (Emotion.Heartbreak, ["heartbreak", "heartbroken", "heartache"]),
        (Emotion.Sadness, ["tears", "tear", "cry", "crying", "cried", "sad", "lonely", "alone", "sorrow", "weep"]),
        (Emotion.Love, ["love", "kiss", "kisses", "darling", "lover", "romance", "adore"]),
        (Emotion.Excitement, ["dance", "dancing", "party", "jump", "wild", "celebrate", "hype", "fun"]),
        (Emotion.Anger, ["hate", "mad", "angry", "rage", "fight", "furious"]),
        (Emotion.Fear, ["scared", "afraid", "fear", "terrified", "nightmare", "panic"]),
        (Emotion.Longing, ["miss", "missing", "wish", "yearn", "someday", "waiting"]),
        (Emotion.Calm, ["calm", "peace", "peaceful", "slow", "breathe", "quiet", "gentle"]),
        (Emotion.Confidence, ["boss", "king", "queen", "crown", "win", "winning", "champion", "unstoppable"]),
        (Emotion.Joy, ["happy", "smile", "smiling", "laugh", "laughing", "joy", "sunshine"]),
    ];

    private static readonly HashSet<string> Breaking = ["broken", "break", "breaking", "broke"];
    private const double SentimentThreshold = 0.4;

    /// <param name="words">The line's words (any case/punctuation).</param>
    /// <param name="sentiment">-1…1 sentiment score for the line.</param>
    public static Emotion EmotionFor(IEnumerable<string> words, double sentiment)
    {
        ArgumentNullException.ThrowIfNull(words);
        var normalized = words.Select(EmojiLexicon.Normalize).ToHashSet();
        if (normalized.Contains("heart") && normalized.Overlaps(Breaking))
        {
            return Emotion.Heartbreak;
        }
        foreach (var (emotion, cue) in Cues)
        {
            if (cue.Overlaps(normalized))
            {
                return emotion;
            }
        }
        if (sentiment > SentimentThreshold)
        {
            return Emotion.Joy;
        }
        return sentiment < -SentimentThreshold ? Emotion.Sadness : Emotion.Neutral;
    }
}
