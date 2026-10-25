using Apmuly.Core.Lyrics;

namespace Apmuly.Core.Tests.Lyrics;

// All lyric text is made-up placeholder text.
public class LrcParserTests
{
    [Fact]
    public void ParsesBasicTimestampedLinesInOrder()
    {
        var parsed = LrcParser.Parse("[00:01.50]first placeholder line\n[00:04.25]second placeholder line");
        Assert.Equal([1.5, 4.25], parsed.Entries.Select(e => e.Start));
        Assert.Equal(["first placeholder line", "second placeholder line"], parsed.Entries.Select(e => e.Text));
    }

    [Theory]
    [InlineData("[01:02]x", 62.0)]
    [InlineData("[01:02.5]x", 62.5)]
    [InlineData("[01:02.05]x", 62.05)]
    [InlineData("[01:02.123]x", 62.123)]
    [InlineData("[10:00.00]x", 600.0)]
    public void SupportsTimestampFormats(string line, double expected)
    {
        var entry = Assert.Single(LrcParser.Parse(line).Entries);
        Assert.Equal(expected, entry.Start, 4);
    }

    [Fact]
    public void ExpandsRepeatedTimestampsAndSorts()
    {
        var parsed = LrcParser.Parse("[00:10.00][00:30.00]repeated hook\n[00:20.00]verse");
        Assert.Equal([10.0, 20, 30], parsed.Entries.Select(e => e.Start));
        Assert.Equal(["repeated hook", "verse", "repeated hook"], parsed.Entries.Select(e => e.Text));
    }

    [Fact]
    public void IgnoresMetadataButAppliesOffset()
    {
        var parsed = LrcParser.Parse("[ar:Someone]\n[ti:Something]\n[offset:+500]\n[00:02.00]line");
        var entry = Assert.Single(parsed.Entries);
        Assert.Equal(1.5, entry.Start);
    }

    [Fact]
    public void KeepsEmptyTimestampedLinesAsGaps()
    {
        var parsed = LrcParser.Parse("[00:01.00]a\n[00:05.00]\n[00:09.00]b");
        Assert.Equal(["a", "", "b"], parsed.Entries.Select(e => e.Text));
    }

    [Fact]
    public void SkipsGarbageHandlesCrlfAndWhitespace()
    {
        var parsed = LrcParser.Parse("hello\r\n[xx:yy]bad\r\n  [00:03.00]  spaced text  \r\n");
        Assert.Equal("spaced text", Assert.Single(parsed.Entries).Text);
    }

    [Fact]
    public void ParsesEnhancedWordTimestamps()
    {
        var entry = LrcParser.Parse("[00:01.00]<00:01.00>alpha <00:01.60>beta <00:02.40>gamma").Entries[0];
        Assert.Equal("alpha beta gamma", entry.Text);
        Assert.Equal(["alpha", "beta", "gamma"], entry.WordStarts!.Select(w => w.Text));
        Assert.Equal([1.0, 1.6, 2.4], entry.WordStarts!.Select(w => w.Start));
    }

    [Fact]
    public void NeverProducesNegativeTimesAfterOffset()
    {
        Assert.Equal(0, LrcParser.Parse("[offset:+3000]\n[00:01.00]early").Entries[0].Start);
    }

    [Theory]
    [InlineData("1:2:3")]
    [InlineData("-1:00")]
    [InlineData("01:x2")]
    [InlineData("")]
    [InlineData("０１:00")]
    public void RejectsMalformedTimestamps(string tag) => Assert.Null(LrcParser.Timestamp(tag));
}
