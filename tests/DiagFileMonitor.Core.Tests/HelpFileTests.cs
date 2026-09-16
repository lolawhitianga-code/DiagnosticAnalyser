using DiagFileMonitor.Core.Help;

namespace DiagFileMonitor.Core.Tests;

public class HelpFileTests
{
    private const string Sample = """
        # Help

        Some preamble that belongs to nothing.

        <!--help:main.analyse-->
        ### Analyse

        Reads the three logs and says what went wrong.

        Select several rows to analyse them together.

        <!--help:main.compare-->
        ### Compare

        Measures the bundle against the benchmark.

        ---

        ## The next window

        <!--help:analysis.copy-->
        ### Copy to clipboard

        Copies the whole report.
        """;

    [Fact]
    public void ReadsEveryTopic()
    {
        var help = HelpFile.Parse(Sample);

        Assert.Equal(3, help.Count);
        Assert.True(help.Has("main.analyse"));
        Assert.True(help.Has("analysis.copy"));
    }

    [Fact]
    public void TheHeadingBecomesTheTitleAndTheRestTheBody()
    {
        var topic = HelpFile.Parse(Sample).Find("main.analyse")!;

        Assert.Equal("Analyse", topic.Title);
        Assert.StartsWith("Reads the three logs", topic.Body);
        Assert.Contains("Select several rows", topic.Body);
        Assert.DoesNotContain("###", topic.Body);
    }

    [Fact]
    public void ASectionBreakEndsATopicRatherThanRunningIntoTheNextWindow()
    {
        // Without this, the last topic of every section swallows the heading that follows it.
        var topic = HelpFile.Parse(Sample).Find("main.compare")!;

        Assert.Equal("Measures the bundle against the benchmark.", topic.Body);
        Assert.DoesNotContain("The next window", topic.Body);
    }

    [Fact]
    public void TextBeforeTheFirstAnchorIsNotATopic()
    {
        Assert.DoesNotContain(HelpFile.Parse(Sample).Topics, t => t.Body.Contains("preamble"));
    }

    [Fact]
    public void AnUnknownIdIsNotAnError()
    {
        var help = HelpFile.Parse(Sample);

        Assert.Null(help.Find("main.nosuchthing"));
        Assert.False(help.Has("main.nosuchthing"));
        Assert.Null(help.Find(null));
        Assert.Null(help.Find("   "));
    }

    [Fact]
    public void IdsAreMatchedWithoutCaringAboutCase()
    {
        Assert.NotNull(HelpFile.Parse(Sample).Find("Main.Analyse"));
    }

    [Fact]
    public void ARepeatedIdKeepsTheFirstOne()
    {
        // A duplicate is a mistake in the file. Letting a later stray replace the real topic would
        // be worse than ignoring it.
        var help = HelpFile.Parse("""
            <!--help:main.analyse-->
            ### Analyse
            The real one.

            <!--help:main.analyse-->
            ### Something else
            A stray.
            """);

        Assert.Equal("The real one.", help.Find("main.analyse")!.Body);
    }

    [Fact]
    public void TheAreaIsThePartBeforeTheDot()
    {
        Assert.Equal("production", new HelpTopic { Id = "production.browse" }.Area);
    }

    [Fact]
    public void AMissingOrUnreadableFileGivesAnEmptyHelpRatherThanThrowing()
    {
        var help = HelpFile.ParseFile(Path.Combine(Path.GetTempPath(), "no-such-help-file.md"));

        Assert.Equal(0, help.Count);
        Assert.Null(help.Find("main.analyse"));
    }

    [Fact]
    public void AnAnchorWithNothingAfterItIsSkipped()
    {
        Assert.Equal(0, HelpFile.Parse("<!--help:main.empty-->\n\n").Count);
    }
}

/// <summary>
/// The shipped help file itself. These stop the file and the app drifting apart - a topic that
/// loses its heading, or an id that changes shape, breaks the popup for that control silently.
/// </summary>
public class ShippedHelpFileTests
{
    private static HelpFile Load()
    {
        // Walk up from the test binary to the repository root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "HELP.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return HelpFile.ParseFile(Path.Combine(dir!.FullName, "docs", "HELP.md"));
    }

    [Fact]
    public void TheShippedFileParses()
    {
        Assert.True(Load().Count >= 60);
    }

    [Fact]
    public void EveryTopicHasATitleAndSomethingToSay()
    {
        foreach (var topic in Load().Topics)
        {
            Assert.False(string.IsNullOrWhiteSpace(topic.Title), $"{topic.Id} has no heading");
            Assert.False(string.IsNullOrWhiteSpace(topic.Body), $"{topic.Id} has no body");
        }
    }

    [Fact]
    public void EveryIdIsLowerCaseAndDotted()
    {
        foreach (var topic in Load().Topics)
        {
            Assert.Equal(topic.Id.ToLowerInvariant(), topic.Id);
            Assert.Contains('.', topic.Id);
        }
    }

    [Fact]
    public void NoTopicIsLongEnoughToOverflowAPopup()
    {
        // These are read in a small window beside a button, not on a page.
        foreach (var topic in Load().Topics)
        {
            var words = topic.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(words <= 160, $"{topic.Id} is {words} words - too long for a popup");
        }
    }
}

public class HelpTextTests
{
    [Fact]
    public void WrappedLinesAreJoinedBackIntoOneParagraph()
    {
        // The file is hard wrapped at 100 characters so it reads well in an editor. A popup is
        // not, so the breaks have to come out or every line ends mid-sentence.
        var blocks = HelpText.Blocks("""
            Reads the three Spida logs in the selected bundle and writes a report
            saying what went wrong.
            """);

        var block = Assert.Single(blocks);
        Assert.Equal("Reads the three Spida logs in the selected bundle and writes a report saying what went wrong.",
            block.Text);
    }

    [Fact]
    public void ABlankLineStartsANewParagraph()
    {
        var blocks = HelpText.Blocks("First thing.\n\nSecond thing.");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("First thing.", blocks[0].Text);
        Assert.Equal("Second thing.", blocks[1].Text);
    }

    [Fact]
    public void BulletsAreTheirOwnBlocksAndKeepNoMarker()
    {
        var blocks = HelpText.Blocks("""
            Some lead in.

            - the first point
            - the second point
            """);

        Assert.Equal(3, blocks.Count);
        Assert.False(blocks[0].IsBullet);
        Assert.True(blocks[1].IsBullet);
        Assert.Equal("the first point", blocks[1].Text);
        Assert.True(blocks[2].IsBullet);
    }

    [Fact]
    public void ABulletRunningOverTwoLinesStaysOneBullet()
    {
        var blocks = HelpText.Blocks("""
            - failed counts bundles that could not be unpacked. Anything above zero
              is worth a look.
            - machines seen is all-time.
            """);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("failed counts bundles that could not be unpacked. Anything above zero is worth a look.",
            blocks[0].Text);
    }

    [Fact]
    public void ABulletDirectlyAfterAParagraphDoesNotSwallowIt()
    {
        var blocks = HelpText.Blocks("Lead in with no blank line after it.\n- a point");

        Assert.Equal(2, blocks.Count);
        Assert.False(blocks[0].IsBullet);
        Assert.True(blocks[1].IsBullet);
    }

    [Fact]
    public void BoldPiecesAlternatePlainThenBold()
    {
        var runs = HelpText.BoldRuns("Press **?**, then click.");

        Assert.Equal(3, runs.Count);
        Assert.Equal("Press ", runs[0]);
        Assert.Equal("?", runs[1]);      // odd index is the bold one
        Assert.Equal(", then click.", runs[2]);
    }

    [Fact]
    public void AnEmptyBodyDrawsNothing()
    {
        Assert.Empty(HelpText.Blocks(null));
        Assert.Empty(HelpText.Blocks("   "));
    }

    [Fact]
    public void EveryShippedTopicSplitsIntoSomethingDrawable()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "HELP.md"))) dir = dir.Parent;

        var help = HelpFile.ParseFile(Path.Combine(dir!.FullName, "docs", "HELP.md"));

        foreach (var topic in help.Topics)
        {
            var blocks = HelpText.Blocks(topic.Body);
            Assert.True(blocks.Count > 0, $"{topic.Id} draws nothing");
            Assert.All(blocks, b => Assert.False(string.IsNullOrWhiteSpace(b.Text)));

            // An unclosed ** would render the rest of the topic bold.
            Assert.True(topic.Body.Split("**").Length % 2 == 1, $"{topic.Id} has an unclosed bold marker");
        }
    }
}
