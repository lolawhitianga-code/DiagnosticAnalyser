using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class StuckStepTests
{
    private static StuckStep? Check(params string[] lines) =>
        StuckStepCheck.Check(MachineLogFile.Parse(lines));

    /// <summary>
    /// A branch taken once, at the end, and never left.
    /// <para>
    /// Note what this check does NOT know: why. On the M21844 bundle this shape is the floating
    /// head obstruction, which is ordinary behaviour going from a taller panel to a shorter one
    /// - so FloatingHeadCheck takes that one and the annotator drops this. The check is still
    /// right about the shape; it is just not the one that should speak.
    /// </para>
    /// </summary>
    [Fact]
    public void CatchesABranchTakenOnceAtTheEnd()
    {
        var stuck = Check(
            "12:40:00.0000000,  Other, WallExtruderStep,  Step = 320",
            "12:40:00.1000000,  Other, WallExtruderStep,  Step = 330",
            "12:43:00.0000000,  Other, WallExtruderStep,  Step = 320",
            "12:43:00.1000000,  Other, WallExtruderStep,  Step = 330",
            "12:46:26.8000000,  Other, WallExtruderStep,  Step = 321",
            "12:46:26.8400000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "12:48:16.6000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "12:50:17.6000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD");

        Assert.NotNull(stuck);
        Assert.Equal(321, stuck!.Step);
        Assert.True(stuck.FirstTimeToday);
        Assert.True(stuck.WorthReporting);
        Assert.Contains("clear Obstacle", stuck.Message);
        Assert.Equal(3, stuck.RepeatsOfMessage);
        Assert.True(stuck.HeldFor > TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// The trap this check exists to avoid. The PLC polls while it waits, so one wait writes a
    /// run of identical lines about 0.18s apart - 39 of them on an M21737 log, across six waits
    /// that all cleared. Counting the lines scores the working machine worse than the stopped
    /// one.
    /// </summary>
    [Fact]
    public void AnInterlockThatKeepsClearingIsNotAFault()
    {
        var lines = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            lines.Add($"09:{i:00}:00.0000000,  Other, WallExtruderStep,  Step = 320");
            lines.Add($"09:{i:00}:00.1000000,  Other, WallExtruderStep,  Step = 321");
            lines.Add($"09:{i:00}:00.3000000,  Other, WallExtruderStep,  Step = 320");
            lines.Add($"09:{i:00}:00.5000000,  Other, WallExtruderStep,  Step = 330");
        }
        lines.Add("09:20:10.0000000,  Other, Eject,  Panel Complete");

        var stuck = Check(lines.ToArray());

        Assert.NotNull(stuck);
        Assert.False(stuck!.WorthReporting);
    }

    /// <summary>A step it takes routinely, but this time it never moved on.</summary>
    [Fact]
    public void CatchesARoutineStepHeldFarTooLong()
    {
        var lines = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            lines.Add($"09:{i:00}:00.0000000,  Other, WallExtruderStep,  Step = 1310");
            lines.Add($"09:{i:00}:01.0000000,  Other, WallExtruderStep,  Step = 1400");
        }
        lines.Add("09:30:00.0000000,  Other, WallExtruderStep,  Step = 1310");
        lines.Add("09:45:00.0000000,  Other, WallExtruderPLC,  Waiting for THNTD");

        var stuck = Check(lines.ToArray());

        Assert.NotNull(stuck);
        Assert.Equal(1310, stuck!.Step);
        Assert.False(stuck.FirstTimeToday);
        Assert.True(stuck.HeldFarTooLong);
        Assert.True(stuck.WorthReporting);
        Assert.Equal(TimeSpan.FromSeconds(1), stuck.TypicalHold);
    }

    /// <summary>
    /// A file taken while the machine is mid-cycle ends on some step or other. Half a second on
    /// it is the export catching it in motion, not a machine that has stopped.
    /// </summary>
    [Fact]
    public void AFileTakenMidCycleIsNotReported()
    {
        var stuck = Check(
            "09:00:00.0000000,  Other, WallExtruderStep,  Step = 300",
            "09:00:01.0000000,  Other, WallExtruderStep,  Step = 302",
            "09:00:01.5000000,  Other, WallExtruderStep,  Step = 304");

        Assert.NotNull(stuck);
        Assert.False(stuck!.WorthReporting);
    }

    /// <summary>
    /// A wait the floating head check can explain must not also be written up here as a machine
    /// that stopped dead. One finding, one voice, and the one that knows why wins.
    /// </summary>
    [Fact]
    public void TheFloatingHeadObstructionIsNotAlsoReportedAsStuck()
    {
        var log = MachineLogFile.Parse(new[]
        {
            "09:00:00.0000000,  Other, FloatingSideHeight,  Move to : 3000",
            "12:40:00.0000000,  Other, WallExtruderStep,  Step = 320",
            "12:40:00.1000000,  Other, WallExtruderStep,  Step = 330",
            "12:46:26.8000000,  Other, WallExtruderStep,  Step = 321",
            "12:46:26.8400000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD",
            "12:50:17.6000000,  Other, REv3,  Unsafe to move Floating Head please clear Obstacle Then Press THNTD"
        });

        Assert.NotNull(StuckStepCheck.Check(log));

        var analysis = new SpidaLogAnalyser().Analyse(
            log, Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(), DateTime.UtcNow);

        var findings = KnowledgeAnnotator.Annotate(analysis, log, "RakingWallExtruderV3DG", "M21844");

        Assert.Null(findings.StuckStep);
        Assert.True(findings.FloatingHead.Any);
    }

    [Fact]
    public void ALogWithNoStepsAtAllIsHandled()
    {
        Assert.Null(Check("09:00:00.0000000,  Other, SharepointReporting,  UploadFile"));
        Assert.Null(StuckStepCheck.Check(Array.Empty<MachineLogEntry>()));
    }
}
