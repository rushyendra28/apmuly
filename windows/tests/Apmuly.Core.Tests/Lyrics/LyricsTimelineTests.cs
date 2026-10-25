using Apmuly.Core.Lyrics;

namespace Apmuly.Core.Tests.Lyrics;

public class LyricsTimelineTests
{
    private static LyricsDocument Document(string lrc, double duration = 200) =>
        LyricsTimeline.Build(LrcParser.Parse(lrc), duration);

    [Fact]
    public void LineEndIsNextLineStart()
    {
        var doc = Document("[00:01.00]one two\n[00:04.00]three\n[00:09.00]four");
        Assert.Equal([4.0, 9.0], doc.Lines.Take(2).Select(l => l.End));
    }

    [Fact]
    public void LastLineIsCappedOrEndsAtTrackEnd()
    {
        Assert.Equal(10 + LyricsTimeline.MaxLastLineDuration, Document("[00:10.00]last", 200).Lines[^1].End);
        Assert.Equal(12, Document("[00:10.00]last", 12).Lines[^1].End);
    }

    [Fact]
    public void EstimatedWordsAreContiguousOrderedAndInsideTheLine()
    {
        var doc = Document("[00:02.00]tiny extraordinary word\n[00:06.00]next");
        var words = doc.Lines[0].Words;
        Assert.Equal(["tiny", "extraordinary", "word"], words.Select(w => w.Text));
        Assert.Equal(2, words[0].Start);
        Assert.All(words, w => Assert.True(w.End > w.Start && w.Start >= 2 && w.End <= 6));
        foreach (var (a, b) in words.Zip(words.Skip(1)))
        {
            Assert.True(Math.Abs(a.End - b.Start) < 0.0001);
        }
        Assert.True(words[1].End - words[1].Start > words[0].End - words[0].Start);
        Assert.Equal(LyricsTiming.LineSynced, doc.Timing);
    }

    [Fact]
    public void LongGapsDoNotStretchShortLines()
    {
        Assert.True(Document("[00:00.00]go\n[00:30.00]next").Lines[0].Words[^1].End < 3);
    }

    [Fact]
    public void EnhancedWordTimingsAreUsedVerbatim()
    {
        var doc = Document("[00:01.00]<00:01.00>alpha <00:01.60>beta\n[00:03.00]next");
        Assert.Equal([1.0, 1.6], doc.Lines[0].Words.Select(w => w.Start));
        Assert.Equal([1.6, 3.0], doc.Lines[0].Words.Select(w => w.End));
        Assert.Equal(LyricsTiming.WordSynced, doc.Timing);
    }

    [Fact]
    public void LastEnhancedWordIsCapped()
    {
        Assert.True(Document("[00:01.00]<00:01.00>alpha <00:01.60>go\n[00:40.00]next").Lines[0].Words[^1].End < 3);
    }

    [Theory]
    [InlineData(0.5, null)]
    [InlineData(1.0, 0)]
    [InlineData(3.99, 0)]
    [InlineData(4.0, 1)]
    [InlineData(100.0, 2)]
    public void FindsTheActiveLine(double position, int? expected)
    {
        Assert.Equal(expected, Document("[00:01.00]a\n[00:04.00]b\n[00:09.00]c").LineIndex(position));
    }

    [Fact]
    public void LineProgressAndActiveWord()
    {
        var line = Document("[00:02.00]alpha beta\n[00:06.00]next").Lines[0];
        Assert.Equal(0, line.Progress(0));
        Assert.Equal(0.5, line.Progress(4));
        Assert.Equal(1, line.Progress(99));
        Assert.Null(line.WordIndex(1.9));
        Assert.Equal(0, line.WordIndex(2.0));
        Assert.Equal(1, line.WordIndex(line.Words[1].Start));
    }

    [Fact]
    public void GapLinesHaveNoWords()
    {
        var doc = Document("[00:01.00]a\n[00:05.00]\n[00:09.00]b");
        Assert.True(doc.Lines[1].IsGap);
        Assert.Empty(doc.Lines[1].Words);
    }

    [Fact]
    public void PlainLyricsBecomeUnsynced()
    {
        var doc = LyricsTimeline.Unsynced("one\n\ntwo\n", 100);
        Assert.Equal(LyricsTiming.Unsynced, doc.Timing);
        Assert.Equal(["one", "two"], doc.Lines.Select(l => l.Text));
    }

    [Fact]
    public void DocumentsCompareByValue()
    {
        Assert.Equal(Document("[00:01.00]a b\n[00:03.00]c"), Document("[00:01.00]a b\n[00:03.00]c"));
        Assert.NotEqual(Document("[00:01.00]a b"), Document("[00:01.00]a c"));
    }
}

public class SyllableTests
{
    [Theory]
    [InlineData("go", 1)]
    [InlineData("hello", 2)]
    [InlineData("extraordinary", 5)]
    [InlineData("time", 1)]
    [InlineData("rhythm", 1)]
    [InlineData("", 1)]
    public void ApproximatesSyllables(string word, int expected) =>
        Assert.Equal(expected, WordTimingEstimator.Syllables(word));

    [Fact]
    public void NonLatinTextGetsAPositiveWeight() =>
        Assert.True(WordTimingEstimator.Syllables("こんにちは") >= 2);
}

public class SingingWeightTests
{
    private static double[] Durations(string text, double start = 0, double end = 10) =>
        WordTimingEstimator.Estimate(text, start, end).Select(w => w.End - w.Start).ToArray();

    [Fact]
    public void LastWordIsHeldLongerThanMidLine()
    {
        var d = Durations("glow glow glow");
        Assert.True(d[2] > d[0] * 1.3);
        Assert.True(Math.Abs(d[0] - d[1]) < 0.0001);
    }

    [Fact]
    public void FillerWordsGoByQuicker()
    {
        var d = Durations("the spark and glow");
        Assert.True(d[0] < d[1]);
        Assert.True(d[2] < d[3]);
    }

    [Fact]
    public void ACommaLengthensTheWordBeforeIt()
    {
        Assert.True(Durations("spark, glow spark")[0] > Durations("spark glow spark")[0]);
    }

    [Fact]
    public void WeightsStayPositiveAndTheLineIsCovered()
    {
        var words = WordTimingEstimator.Estimate("a the of to", 5, 6);
        Assert.All(words, w => Assert.True(w.End > w.Start));
        Assert.True(Math.Abs(words[^1].End - 6) < 0.0001);
        Assert.All(WordTimingEstimator.Weights(["a", "glow"]), w => Assert.True(w > 0));
    }
}
