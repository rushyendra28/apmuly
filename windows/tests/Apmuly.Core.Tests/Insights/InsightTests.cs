using System.Text;
using Apmuly.Core.Insights;
using Apmuly.Core.Lyrics;

namespace Apmuly.Core.Tests.Insights;

// All line text below is made-up placeholder text.
public class EmojiLexiconTests
{
    [Theory]
    [InlineData("moon", "🌙")]
    [InlineData("Moon", "🌙")]
    [InlineData("roses", "🌹")]
    [InlineData("cars", "🚗")]
    [InlineData("cities", "🏙️")]
    [InlineData("glasses", "👓")]
    [InlineData("pizza", "🍕")]
    [InlineData("whale", "🐳")]
    [InlineData("headphones", "🎧")]
    [InlineData("castle", "🏰")]
    public void MapsObjectsIncludingPlurals(string word, string expected) => Assert.Equal(expected, EmojiLexicon.EmojiForObject(word));

    [Theory]
    [InlineData("dance", "💃")]
    [InlineData("dancing", "💃")]
    [InlineData("cry", "😭")]
    [InlineData("crying", "😭")]
    [InlineData("fly", "🕊️")]
    [InlineData("sing", "🎤")]
    public void MapsActions(string word, string expected) => Assert.Equal(expected, EmojiLexicon.EmojiForAction(word));

    [Fact]
    public void HasALargeVocabulary()
    {
        Assert.True(EmojiLexicon.ObjectCount >= 250);
        Assert.True(EmojiLexicon.ActionCount >= 30);
    }

    [Theory]
    [InlineData("quantum")]
    [InlineData("time")]
    [InlineData("way")]
    [InlineData("")]
    public void UnknownAndAbstractWordsHaveNoObjectEmoji(string word) => Assert.Null(EmojiLexicon.EmojiForObject(word));

    [Fact]
    public void EveryEmotionExceptNeutralHasEmoji()
    {
        foreach (var emotion in Enum.GetValues<Emotion>().Where(e => e != Emotion.Neutral))
        {
            Assert.NotNull(EmojiLexicon.EmojiFor(emotion, 0));
        }
        Assert.Null(EmojiLexicon.EmojiFor(Emotion.Neutral, 0));
        Assert.NotNull(EmojiLexicon.EmojiFor(Emotion.Joy, int.MinValue));
    }

    [Theory]
    [InlineData("time")]
    [InlineData("life")]
    [InlineData("feeling")]
    [InlineData("way")]
    public void AbstractWordsAreRecognised(string word) => Assert.True(EmojiLexicon.IsAbstract(word));

    [Fact]
    public void EveryEmojiIsUnicode13OrEarlierWithoutZwj()
    {
        foreach (var emoji in EmojiLexicon.AllEmoji)
        {
            Assert.DoesNotContain('‍', emoji);
            Assert.All(emoji.EnumerateRunes(), r => Assert.True(r.Value < 0x1FA70 || r.Value is >= 0x1FA70 and <= 0x1FAD6,
                $"{emoji} may be newer than Unicode 13"));
        }
    }
}

public class EmotionLexiconTests
{
    [Theory]
    [InlineData(new[] { "tears", "fall" }, 0.5, Emotion.Sadness)]
    [InlineData(new[] { "dance", "all", "night" }, -0.2, Emotion.Excitement)]
    [InlineData(new[] { "broken", "heart" }, 0.0, Emotion.Heartbreak)]
    [InlineData(new[] { "kiss", "me" }, 0.0, Emotion.Love)]
    public void KeywordCuesWinOverSentiment(string[] words, double sentiment, Emotion expected) =>
        Assert.Equal(expected, EmotionLexicon.EmotionFor(words, sentiment));

    [Theory]
    [InlineData(0.8, Emotion.Joy)]
    [InlineData(-0.7, Emotion.Sadness)]
    [InlineData(0.1, Emotion.Neutral)]
    public void FallsBackToSentiment(double score, Emotion expected) =>
        Assert.Equal(expected, EmotionLexicon.EmotionFor(["plain", "words"], score));
}

public class InsightPlannerTests
{
    private static LyricLine Line(double start, params string[] words) =>
        new(start, start + words.Length, string.Join(' ', words),
            words.Select((w, i) => new LyricWord(w, start + i, start + i + 1)).ToArray());

    private static LineReading Reading(Emotion emotion, params string[] objects) => new(emotion, objects);

    [Fact]
    public void EveryExpressiveLineGetsAnEmojiAndMoodChangesBurst()
    {
        LyricLine[] doc = [Line(0, "a"), Line(2, "b"), Line(4, "c"), Line(6, "d")];
        var insights = InsightPlanner.Plan(doc, new Dictionary<int, LineReading>
        {
            [0] = Reading(Emotion.Joy), [1] = Reading(Emotion.Joy), [2] = Reading(Emotion.Sadness), [3] = Reading(Emotion.Neutral),
        });
        Assert.NotNull(insights[0].Emoji);
        Assert.True(insights[0].Burst);
        Assert.NotNull(insights[1].Emoji);
        Assert.False(insights[1].Burst);
        Assert.True(insights[2].Burst);
        Assert.False(insights.ContainsKey(3));
    }

    [Fact]
    public void ObjectsGetEmojiAndTheirWordIndex()
    {
        var insights = InsightPlanner.Plan([Line(10, "under", "the", "moons", "and", "lighthouse")],
            new Dictionary<int, LineReading> { [0] = Reading(Emotion.Neutral, "Moon", "lighthouse") });
        var objects = insights[0].Objects;
        Assert.Equal(["moon", "lighthouse"], objects.Select(o => o.Word));
        Assert.Equal("🌙", objects[0].Emoji);
        Assert.Equal(2, objects[0].WordIndex);
        Assert.Null(objects[1].Emoji);
        Assert.Equal(4, objects[1].WordIndex);
    }

    [Fact]
    public void LineWordsAddEmojiWithoutAnalyzerObjects()
    {
        var objects = InsightPlanner.Plan([Line(0, "we", "dance", "under", "stars")],
            new Dictionary<int, LineReading> { [0] = Reading(Emotion.Neutral) })[0].Objects;
        Assert.Equal(["dance", "star"], objects.Select(o => o.Word));
        Assert.Equal("💃", objects[0].Emoji);
        Assert.Equal(1, objects[0].WordIndex);
        Assert.Equal("⭐", objects[1].Emoji);
    }

    [Fact]
    public void DropsAbstractInvalidAndDuplicateObjectsAndCaps()
    {
        var objects = InsightPlanner.Plan([Line(0, "x")], new Dictionary<int, LineReading>
        {
            [0] = Reading(Emotion.Neutral, "time", "car", "car", "rose", "sun", "moon", "<script>", ""),
        })[0].Objects;
        Assert.Equal(["car", "rose", "sun"], objects.Select(o => o.Word));
    }

    [Fact]
    public void TheSameObjectSkipsOnlyTheNextLine()
    {
        LyricLine[] doc = [Line(0, "a"), Line(2, "b"), Line(4, "c")];
        var readings = Enumerable.Range(0, 3).ToDictionary(i => i, _ => Reading(Emotion.Neutral, "star"));
        var insights = InsightPlanner.Plan(doc, readings);
        Assert.Equal("star", insights[0].Objects[0].Word);
        Assert.False(insights.ContainsKey(1));
        Assert.Equal("star", insights[2].Objects[0].Word);
    }

    [Fact]
    public void GapLinesAndMissingReadingsProduceNothing()
    {
        LyricLine[] gaps = [new(0, 4, "", []), new(4, 8, "", [])];
        Assert.Empty(InsightPlanner.Plan(gaps, new Dictionary<int, LineReading> { [0] = Reading(Emotion.Joy, "sun") }));
    }

    [Fact]
    public void LexiconAnalyzerReadsKeywordEmotionsAndLineObjects()
    {
        var doc = new LyricsDocument([Line(0, "tears", "on", "the", "moon"), Line(5, "plain", "words"), new LyricLine(8, 9, "", [])],
                                     LyricsTiming.LineSynced);
        var insights = LexiconAnalyzer.Insights(doc);
        Assert.NotNull(insights[0].Emoji);
        Assert.Contains(insights[0].Objects, o => o.Word == "moon");
        Assert.False(insights.ContainsKey(1));
        Assert.False(insights.ContainsKey(2));
    }
}

public class LineDecorationTests
{
    [Fact]
    public void EmotionFirstThenUniqueObjectsCapped()
    {
        var insight = new LineInsight("😍", false, [
            new ObjectCue("moon", "🌙", 0), new ObjectCue("lighthouse", null, 1), new ObjectCue("star", "⭐", 2), new ObjectCue("stars", "⭐", 3),
        ]);
        Assert.Equal("😍 🌙 ⭐", LineDecoration.EmojiFor(insight));
        Assert.Equal("", LineDecoration.EmojiFor(null));
    }
}
