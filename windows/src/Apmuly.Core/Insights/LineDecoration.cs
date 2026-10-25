namespace Apmuly.Core.Insights;

/// <summary>The emoji shown at the end of a line in Lyrics mode: mood first, then the things it mentions.</summary>
public static class LineDecoration
{
    public const int MaxEmoji = 3;

    public static string EmojiFor(LineInsight? insight)
    {
        if (insight is null)
        {
            return string.Empty;
        }
        var seen = new HashSet<string>();
        var emoji = new[] { insight.Emoji }
            .Concat(insight.Objects.Select(o => o.Emoji))
            .OfType<string>()
            .Where(seen.Add)
            .Take(MaxEmoji);
        return string.Join(' ', emoji);
    }
}
