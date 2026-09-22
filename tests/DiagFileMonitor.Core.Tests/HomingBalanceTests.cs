using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class HomingBalanceTests
{
    private static HomingFindings Check(params string[] lines) =>
        HomingBalanceCheck.Check(MachineLogFile.Parse(lines));

    private static string[] Homing(string start, double fixedAfter, double floatingAfter)
    {
        var t0 = TimeSpan.Parse(start);
        string At(double s) => (t0 + TimeSpan.FromSeconds(s)).ToString(@"hh\:mm\:ss\.fffffff");

        var done = new[] { (fixedAfter, "Node0"), (floatingAfter, "Node1") }.OrderBy(x => x.Item1);

        return new[]
            {
                $"{At(0)},  MotionEvent, Node1 Status,  Homing",
                $"{At(0)},  MotionEvent, Node0 Status,  Homing"
            }
            .Concat(done.Select(d => $"{At(d.Item1)},  MotionEvent, {d.Item2} Status,  OK"))
            .ToArray();
    }

    /// <summary>
    /// M21856 at Mainland: the fixed side gripper out by 30 mm, the fixed side home sensor too
    /// far from its aluminium block. The fixed side finished homing 2.17 s after the floating.
    /// </summary>
    [Fact]
    public void CatchesTheM21856FixedSideSensor()
    {
        var findings = Check(Homing("07:45:18.00", 39.82, 37.65));

        var run = Assert.Single(findings.Unbalanced);
        Assert.Equal("fixed", run.LateSide);
        Assert.Equal(2.17, run.Gap, 2);
    }

    /// <summary>Every healthy full homing run in the logs finishes both sides in the same millisecond.</summary>
    [Fact]
    public void AHealthyMachineHomesBothSidesTogether()
    {
        var findings = Check(Homing("06:00:00.00", 37.58, 37.58));

        Assert.Single(findings.Runs);
        Assert.False(findings.Any);
    }

    /// <summary>
    /// A re-home from nearly home wobbles by up to 0.8 s between the sides and means nothing -
    /// the axes barely moved, so a late sensor has nowhere to show.
    /// </summary>
    [Fact]
    public void AShortReHomeIsNotJudged()
    {
        var findings = Check(Homing("06:00:00.00", 1.18, 0.39));

        Assert.False(Assert.Single(findings.Runs).Full);
        Assert.False(findings.Any);
    }

    /// <summary>The same signature on the other side, as in M20771's September 2026 export.</summary>
    [Fact]
    public void NamesTheFloatingSideWhenItIsTheLateOne()
    {
        var run = Assert.Single(Check(Homing("06:00:00.00", 40.54, 41.94)).Unbalanced);

        Assert.Equal("floating", run.LateSide);
    }

    [Fact]
    public void ALogWithNoHomingHasNothingToSay() =>
        Assert.Empty(Check("09:00:00.0000000,  Other, WallExtruderStep,  Step = 10").Runs);
}
