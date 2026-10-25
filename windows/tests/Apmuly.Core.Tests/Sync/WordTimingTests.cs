using Apmuly.Core.Lyrics;
using Apmuly.Core.Sync;
using Apmuly.Core.Tests.Support;

namespace Apmuly.Core.Tests.Sync;

// All words below are made-up placeholder text.
public class WordRetimerTests
{
    private static LyricLine Line(double start, double end, params string[] words)
    {
        var text = string.Join(' ', words);
        return new LyricLine(start, end, text, WordTimingEstimator.Estimate(text, start, end));
    }

    private static readonly LyricsDocument Doc = new(
        [Line(10, 20, "glow", "pop", "zoom", "spark", "drift"), Line(20, 30, "mint", "plume")], LyricsTiming.LineSynced);

    private static double[] Starts(LyricsDocument d, int i) => d.Lines[i].Words.Select(w => w.Start).ToArray();

    [Fact]
    public void PinsHeardWordsAndSpreadsTheOthers()
    {
        var retimed = WordRetimer.Retime(Doc, [new WordObservation(0, 1, 11.0), new WordObservation(0, 3, 15.0)]);
        var s = Starts(retimed, 0);
        Assert.Equal(11.0, s[1]);
        Assert.Equal(15.0, s[3]);
        Assert.True(s[0] >= 10 && s[0] < 11);
        Assert.True(s[2] > 11 && s[2] < 15);
        Assert.True(s[4] > 15 && s[4] < 20);
        var words = retimed.Lines[0].Words;
        foreach (var (a, b) in words.Zip(words.Skip(1)))
        {
            Assert.True(Math.Abs(a.End - b.Start) < 0.0001);
        }
        Assert.All(words, w => Assert.True(w.End > w.Start));
    }

    [Fact]
    public void ContinuesAtTheMeasuredPace()
    {
        var s = Starts(WordRetimer.Retime(Doc, [new WordObservation(0, 0, 10.0), new WordObservation(0, 1, 11.0)]), 0);
        Assert.True(Math.Abs(s[2] - 12.0) < 0.01);
        Assert.True(Math.Abs(s[3] - 13.0) < 0.01);
    }

    [Fact]
    public void RejectsOutOfOrderOrOutOfLineObservations()
    {
        var s = Starts(WordRetimer.Retime(Doc, [
            new WordObservation(0, 1, 13.0),
            new WordObservation(0, 2, 12.0),
            new WordObservation(0, 4, 25.0),
        ]), 0);
        Assert.Equal(13.0, s[1]);
        Assert.True(s[2] > 13.0);
        Assert.True(s[4] < 20.0);
    }

    [Fact]
    public void EarlyObservationsAreClampedToTheLineStart() =>
        Assert.Equal(10.0, Starts(WordRetimer.Retime(Doc, [new WordObservation(0, 0, 9.4)]), 0)[0]);

    [Fact]
    public void ARepeatedLineInheritsLearnedTiming()
    {
        var chorus = new LyricsDocument([Line(10, 14, "glow", "pop", "zoom"), Line(14, 20, "mint", "plume"), Line(40, 44, "glow", "pop", "zoom")],
                                        LyricsTiming.LineSynced);
        var s = Starts(WordRetimer.Retime(chorus, [new WordObservation(0, 1, 11.5), new WordObservation(0, 2, 12.5)]), 2);
        Assert.True(Math.Abs(s[1] - 41.5) < 0.0001);
        Assert.True(Math.Abs(s[2] - 42.5) < 0.0001);
    }

    [Fact]
    public void LinesWithoutObservationsAreUntouched()
    {
        Assert.Equal(Doc, WordRetimer.Retime(Doc, []));
        Assert.Equal(Doc.Lines[1], WordRetimer.Retime(Doc, [new WordObservation(0, 1, 11.0)]).Lines[1]);
    }
}

public sealed class TimingStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SavesLoadsAndMergesPerTrackNewestWins()
    {
        var store = new TimingStore(_dir.Path);
        store.Save([new WordObservation(0, 1, 11.0), new WordObservation(2, 0, 30.0)], "T1");
        store.Save([new WordObservation(0, 1, 11.2)], "T1");
        Assert.Equal([new WordObservation(0, 1, 11.2), new WordObservation(2, 0, 30.0)], store.Load("T1"));
        Assert.Empty(store.Load("other"));
    }

    [Fact]
    public void CorruptFilesLoadAsEmpty()
    {
        var store = new TimingStore(_dir.Path);
        store.Save([new WordObservation(0, 1, 11.0)], "T1");
        File.WriteAllText(Directory.GetFiles(_dir.Path)[0], "{oops");
        Assert.Empty(store.Load("T1"));
    }
}
