using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class StuckStepTests
{
    private static StuckStep? Check(params string[] lines) =>
        StuckStepCheck.Check(MachineLogFile.Parse(lines));

    /// <summary>
    /// The M21844 shape. The eject sequence ran 300, 302, 320, 330 all day; on the last one it
    /// went 320 then 321 instead and stayed on 321 until the file was taken four minutes later.
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
    /// The trap this check exists to avoid. The same interlock fired 39 times on M21737 and
    /// cleared in about two tenths of a second each time - that is a working machine, and
    /// counting the message would have scored it worse than the one that was genuinely stuck.
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

    [Fact]
    public void ALogWithNoStepsAtAllIsHandled()
    {
        Assert.Null(Check("09:00:00.0000000,  Other, SharepointReporting,  UploadFile"));
        Assert.Null(StuckStepCheck.Check(Array.Empty<MachineLogEntry>()));
    }
}
