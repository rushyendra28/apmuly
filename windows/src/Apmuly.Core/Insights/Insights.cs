namespace Apmuly.Core.Insights;

/// <summary>The dominant feeling or expression of a lyric line.</summary>
public enum Emotion
{
    Joy,
    Love,
    Sadness,
    Anger,
    Excitement,
    Calm,
    Fear,
    Confidence,
    Heartbreak,
    Longing,
    Neutral,
}

/// <summary>What an analyzer read from one line.</summary>
/// <param name="Objects">Concrete things the line mentions, as the analyzer named them (unvalidated).</param>
public sealed record LineReading(Emotion Emotion, IReadOnlyList<string> Objects);

/// <summary>Something mentioned in a line that the stage can picture.</summary>
/// <param name="Word">Normalised word (lowercase, singular).</param>
/// <param name="Emoji">Emoji for the object, if one fits.</param>
/// <param name="WordIndex">Index of the word in the line that mentions it (so it appears as it's sung).</param>
public sealed record ObjectCue(string Word, string? Emoji, int? WordIndex);

/// <summary>What to show alongside one line.</summary>
/// <param name="Emoji">Emotion emoji for the line (every expressive line has one).</param>
/// <param name="Burst">True when the mood just changed — the stage celebrates with an emoji shower.</param>
public sealed record LineInsight(string? Emoji, bool Burst, IReadOnlyList<ObjectCue> Objects);
