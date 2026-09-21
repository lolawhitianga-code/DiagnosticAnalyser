using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class FloatingHeadTests
{
    private static FloatingHeadFindings Check(params string[] lines) =>
        FloatingHeadCheck.Check(MachineLogFile.Parse(lines));

    /// <summary>
    /// One wait, built the way the machine writes one: the sequencer polling 320/321 every
    /// fraction of a second while it is blocked, then clearing through to 330.
    /// </summary>
    private static string[] Wait(string at, int polls, string heightBefore, string? heightAfter)
    {
        var lines = new List<string>
        {
            $"09:00:00.0000000,  Other, FloatingSideHeight,  Move to : {heightBefore}"
        };

        for (var i = 0; i < polls; i++)
        {
            lines.Add($"{at}.{i * 2:0000000},  Other, WallExtruderStep,  Step = 320");
            lines.Add($"{at}.{i * 2 + 1:0000000},  Other, WallExtruderStep,  Step = 321");
            lines.Add($"{at}.{i * 2 + 1:0000000},  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD");
        }

        if (heightAfter is not null)
        {
            lines.Add($"{at}.9000000,  Other, WallExtruderStep,  Step = 330");
            lines.Add($"{at}.9500000,  Other, FloatingSideHeight,  Move to : {heightAfter}");
        }

        lines.Add("09:40:00.0000000,  Other, Eject,  Panel Complete");
        return lines.ToArray();
    }

    /// <summary>
    /// The ordinary case, and the one that matters most to get right. A 3m panel followed by a
    /// 2m panel leaves the hand-set pieces for the 3m one standing in the way of the head coming
    /// in. The machine stopping there is the machine working, and calling it a fault sends a
    /// technician to a site where nothing is broken.
    /// </summary>
    [Fact]
    public void ALowerNextPanelIsNormalAndNotFlagged()
    {
        var findings = Check(Wait("09:10:00", 5, "3000", "2000"));

        var episode = Assert.Single(findings.Episodes);
        Assert.True(episode.ExplainedByALowerPanel);
        Assert.Equal(-1000, episode.HeightChange);
        Assert.Empty(findings.WorthALook);
    }

    /// <summary>
    /// The case actually worth a phone call: the head was being asked to go OUT, not in, so no
    /// hand-set piece explains the obstruction, and it held the machine up for minutes rather
    /// than clearing.
    /// </summary>
    [Fact]
    public void ALongWaitWithNoHeightDropIsFlagged()
    {
        var findings = Check(
            "09:00:00.0000000,  Other, FloatingSideHeight,  Move to : 2000",
            "09:10:00.0000000,  Other, WallExtruderStep,  Step = 321",
            "09:10:00.1000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "09:14:00.0000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "09:14:30.0000000,  Other, WallExtruderStep,  Step = 330",
            "09:14:31.0000000,  Other, FloatingSideHeight,  Move to : 3000");

        var episode = Assert.Single(findings.WorthALook);
        Assert.False(episode.ExplainedByALowerPanel);
        Assert.Equal(1000, episode.HeightChange);
        Assert.True(episode.Lasted >= TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The same thing over in a moment is not worth anyone's time, height rise or not. Being
    /// asked about a one second wait four days later helps nobody.
    /// </summary>
    [Fact]
    public void AQuickWaitIsRoutineWhicheverWayTheHeightWent()
    {
        var findings = Check(Wait("09:10:00", 5, "2000", "3000"));

        Assert.Empty(findings.WorthALook);
    }

    /// <summary>
    /// The number support actually wants. This is a guard doing its job, not a fault, so what
    /// matters is what it costs - and on both real V3 logs that is well under 1% of the shift.
    /// </summary>
    [Fact]
    public void AddsUpTheTimeTheGuardCost()
    {
        var findings = Check(
            "08:00:00.0000000,  Other, WallExtruderStep,  Step = 10",
            "08:00:00.1000000,  Other, FloatingSideHeight,  Move to : 3000",
            "08:10:00.0000000,  Other, WallExtruderStep,  Step = 321",
            "08:10:00.1000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "08:10:30.0000000,  Other, WallExtruderStep,  Step = 330",
            "08:10:31.0000000,  Other, FloatingSideHeight,  Move to : 2000",
            "09:00:00.0000000,  Other, WallExtruderStep,  Step = 321",
            "09:00:00.1000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "09:00:10.0000000,  Other, WallExtruderStep,  Step = 330",
            "10:00:00.0000000,  Other, Eject,  Panel Complete");

        Assert.Equal(2, findings.Episodes.Count);
        Assert.Equal(TimeSpan.FromSeconds(40), findings.TotalTime);
        Assert.Equal(TimeSpan.FromSeconds(30), findings.Longest);
        Assert.Equal(40.0 / 7200, findings.ShareOfShift!.Value, 4);
    }

    /// <summary>
    /// Step 0 is the operator taking it back to the start rather than clearing it. Worth
    /// counting separately - it is the one outcome where the wait cost a cycle as well as time.
    /// </summary>
    [Fact]
    public void CountsTheOnesTheOperatorGaveUpOn()
    {
        var findings = Check(
            "05:00:00.0000000,  Other, FloatingSideHeight,  Move to : 2570",
            "05:16:52.0000000,  Other, WallExtruderStep,  Step = 321",
            "05:16:52.1000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "05:17:38.0000000,  Other, WallExtruderStep,  Step = 0",
            "05:20:00.0000000,  Other, Eject,  Panel Complete");

        var episode = Assert.Single(findings.Episodes);
        Assert.True(episode.Abandoned);
        Assert.False(episode.Cleared);
        Assert.Equal(1, findings.AbandonedCount);
    }

    /// <summary>
    /// The PLC polls steps 320 and 321 about every 0.18s while it is blocked, so one wait writes
    /// a run of identical lines. Counting the lines makes a five second wait look like thirty
    /// faults; the number that means something is how long the wait ran.
    /// </summary>
    [Fact]
    public void APollLoopIsOneWaitNotManyFaults()
    {
        var findings = Check(Wait("09:10:00", 30, "3000", "2000"));

        var episode = Assert.Single(findings.Episodes);
        Assert.Equal(30, episode.Complaints);
    }

    /// <summary>
    /// The HMI only repeats the message when something else happens, so a four minute wait can
    /// show two complaints two minutes apart. Splitting on a time gap would report that as three
    /// separate waits; the wait ends when the sequencer leaves the 320/321 poll, not before.
    /// </summary>
    [Fact]
    public void AQuietStretchMidWaitDoesNotSplitIt()
    {
        var findings = Check(
            "09:00:00.0000000,  Other, FloatingSideHeight,  Move to : 2570",
            "12:46:26.8000000,  Other, WallExtruderStep,  Step = 321",
            "12:46:26.8400000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "12:48:16.6000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "12:50:17.6000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD");

        var episode = Assert.Single(findings.Episodes);
        Assert.Equal(3, episode.Complaints);
        Assert.True(episode.Lasted > TimeSpan.FromMinutes(3));
        Assert.True(episode.LogEndedDuringIt);
        Assert.False(episode.Cleared);
    }

    /// <summary>
    /// The first move in of a shift has no earlier height target to compare against. Clearing in
    /// a few seconds and carrying on is the machine working, whatever we can say about heights.
    /// </summary>
    [Fact]
    public void AQuickOneThatClearsIsRoutineEvenWithNoHeightToCompare()
    {
        var findings = Check(
            "04:21:59.0000000,  Other, WallExtruderStep,  Step = 321",
            "04:21:59.1000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "04:22:09.0000000,  Other, WallExtruderStep,  Step = 330",
            "04:30:00.0000000,  Other, Eject,  Panel Complete");

        var episode = Assert.Single(findings.Episodes);
        Assert.Null(episode.HeightChange);
        Assert.True(episode.Cleared);
        Assert.Empty(findings.WorthALook);
    }

    [Fact]
    public void ALogThatNeverSaysItHasNothingToReport()
    {
        Assert.False(Check("09:00:00.0000000,  Other, WallExtruderStep,  Step = 330").Any);
        Assert.False(FloatingHeadCheck.Check(Array.Empty<MachineLogEntry>()).Any);
    }
}
