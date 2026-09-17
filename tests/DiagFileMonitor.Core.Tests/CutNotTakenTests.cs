using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// Built from the real M22215 (SprintM600) export from Akarana Timbers, where the operator wrote
/// "manual to 335 thntd, no action". Every line here is copied from that log.
/// </summary>
public class CutNotTakenTests
{
    private static CutNotTakenFindings Check(params string[] lines) =>
        CutNotTakenCheck.Check(MachineLogFile.Parse(lines));

    /// <summary>A cut that ran all the way through, as that machine logs one.</summary>
    private static string[] Cut(string hhmmss) => new[]
    {
        $"{hhmmss}.0000000,  Other, Cutmode,  Board",
        $"{hhmmss}.1000000,  Other, BladeCutStep,  Step = 10",
        $"{hhmmss}.2000000,  Other, BladeCutStep,  Step = 20",
        $"{hhmmss}.3000000,  Other, BladeCutStep,  Step = 30",
        $"{hhmmss}.4000000,  Other, BladeCutStep,  Step = 40",
        $"{hhmmss}.5000000,  Other, BladeCutStep,  Step = 0"
    };

    /// <summary>
    /// The operator asking and getting nothing: cut mode drops to None, the trolley is sent to a
    /// position and gets there, and then the log says nothing at all until the lid is opened.
    /// </summary>
    private static string[] Attempt(string request, string arrived, string gaveUp, string target) => new[]
    {
        $"{request}.5148457,  Other, Cutmode,  None",
        $"{request}.5148457,  Other, ClsTrolleyPusher,  Move: Attempting move to {target}",
        $"{request}.5312616,  Other, Trolley,  Move to : {target}",
        $"{request}.5572607,  Other, SawRotation,  Move to : 90",
        $"{arrived}.7623228,  MotionEvent, Node0 Status,  OK",
        $"{gaveUp}.8196950,  InputChange, LidOpenRequest,  Input (192.168.250.1-1.6) Changed to 1"
    };

    private static string[] TheRealCase() => Cut("08:54:47")
        .Concat(Attempt("08:56:34", "08:56:35", "08:56:51", "604.1"))
        .Concat(Attempt("08:58:06", "08:58:09", "08:58:11", "5004.1"))
        .Concat(Attempt("08:59:58", "09:00:00", "09:00:13", "339.1"))
        .ToArray();

    [Fact]
    public void FindsTheMachineBeingPositionedWithNoCutFollowing()
    {
        var findings = Check(TheRealCase());

        Assert.True(findings.Any);
        Assert.Equal(3, findings.Waits.Count);
        Assert.Equal(1, findings.CutCycles);
        Assert.Equal(new TimeSpan(0, 8, 54, 47, 500), findings.LastCutAt!.Value);
    }

    [Fact]
    public void TheSilenceIsTheFinding()
    {
        // The last attempt: in position at 09:00:00.762, and not one line written for 13 seconds
        // until the operator opened the lid. That is what "thntd, no action" looks like in a log.
        var last = Check(TheRealCase()).Waits[^1];

        Assert.Equal(0, last.EntriesWhileWaiting);
        Assert.Equal(13.1, last.Waited.TotalSeconds, 1);
        Assert.Equal("the lid was opened", last.GaveUpBy);
    }

    [Fact]
    public void ReportsThePositionTheOperatorAskedFor()
    {
        // The trolley and the saw rotation are written 26ms apart and are one request. Splitting
        // them reports "SawRotation 90" as what was asked for, when the position that matters -
        // and the one the operator quotes - is the trolley's.
        var last = Check(TheRealCase()).Waits[^1];

        Assert.Equal("Trolley 339.1, SawRotation 90", last.Describe());
    }

    [Fact]
    public void SaysWhenTheMachineCannotSeeTheOperatorAskingAtAll()
    {
        // On this machine the two-hand buttons go straight into the PLC, so the software only
        // sees a press the PLC has accepted. Silence cannot be read as "nobody pressed it".
        Assert.False(Check(TheRealCase()).TwoHandEverLogged);

        var withThntd = TheRealCase()
            .Append("08:57:00.0000000,  InputChange, THNTD,  Input (COM7-2.12) Changed to 1")
            .ToArray();

        Assert.True(Check(withThntd).TwoHandEverLogged);
    }

    [Fact]
    public void CountsWhichCutModeTheMachineActuallyCutUnder()
    {
        var findings = Check(TheRealCase());

        Assert.Equal("None", findings.CutModeDuringWaits);
        Assert.Equal(1, findings.CutModeAtCutStart["Board"]);
        Assert.Contains("None", findings.CutModesThatNeverCut);
        Assert.DoesNotContain("Board", findings.CutModesThatNeverCut);
    }

    [Fact]
    public void AShortBurstFromRaisingTheBladeByHandIsNotACut()
    {
        // Real lines. Pressing SawUpPB logs BladeCutStep 7, 5, 7, 12 - nowhere near a full cycle.
        // Counting those as cuts would move "the last cut" forward and hide the silence after it.
        var findings = Check(Cut("08:54:47")
            .Concat(new[]
            {
                "08:56:27.9182145,  InputChange, SawUpPB,  Input (192.168.250.1-1.1) Changed to 1",
                "08:56:27.9182145,  Other, BladeCutStep,  Step = 7",
                "08:56:27.9502144,  Other, BladeCutStep,  Step = 5",
                "08:56:28.9202318,  Other, BladeCutStep,  Step = 7",
                "08:56:28.9232318,  Other, BladeCutStep,  Step = 12"
            })
            .Concat(Attempt("08:58:06", "08:58:09", "08:58:11", "5004.1"))
            .Concat(Attempt("08:59:58", "09:00:00", "09:00:13", "339.1"))
            .ToArray());

        Assert.Equal(1, findings.CutCycles);
        Assert.Equal(new TimeSpan(0, 8, 54, 47, 500), findings.LastCutAt!.Value);
        Assert.Equal(2, findings.Waits.Count);
    }

    [Fact]
    public void AMachineBeingPutAwayAtTheEndOfAShiftIsNotAComplaint()
    {
        // The control. On the 100,000 line M20716 saw log the wind-down leaves five idle moves
        // behind and not one of them is retried. Without this the check cries wolf on every
        // normal export.
        var findings = Check(Cut("13:06:53")
            .Concat(new[]
            {
                "13:06:54.7570000,  Other, InfeedFollower,  Move to : 4268.5751953125",
                "13:07:20.7280000,  Other, SawHeight(Z),  Move to : 160",
                "13:07:52.0640000,  Other, InBelt(X1),  Move to : -6000",
                "13:08:20.1590000,  Other, InfeedFollower,  Move to : 4399.5752",
                "13:09:13.3133406,  Other, SharepointReporting,  Upload succeeded"
            })
            .ToArray());

        Assert.False(findings.Any);
        Assert.Empty(findings.Waits);

        // It still did the measuring, so the workings are visible when somebody asks why.
        Assert.Equal(1, findings.CutCycles);
        Assert.Equal(4, findings.MovesAfterLastCut);
    }

    [Fact]
    public void OneWaitOnItsOwnIsNotAPattern()
    {
        var findings = Check(Cut("08:54:47")
            .Concat(Attempt("08:59:58", "09:00:00", "09:00:13", "339.1"))
            .ToArray());

        Assert.False(findings.Any);
        Assert.Single(findings.Waits);
    }

    [Fact]
    public void AMachineThatDoesNotCutIsNotAskedAboutCutting()
    {
        // A raked wall extruder has no BladeCutStep at all. Nothing here applies to it.
        var findings = Check(
            "07:53:30.1000000,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On",
            "07:53:31.3000000,  InputChange, THNTD,  Input (COM7-2.12) Changed to 1",
            "07:53:38.9085106,  Other, WallExtruderStep,  Step = 0");

        Assert.False(findings.Any);
        Assert.Equal(0, findings.CutCycles);
    }

    [Fact]
    public void ResetCountsAsGivingUpAsWellAsTheLid()
    {
        var findings = Check(Cut("08:54:47")
            .Concat(new[]
            {
                "08:56:34.7986661,  Other, Cutmode,  None",
                "08:56:34.8601748,  Other, Trolley,  Move to : 604.1",
                "08:56:35.6900000,  MotionEvent, Node0 Status,  OK",
                "08:56:38.3660000,  InputChange, ResetPB,  Input (192.168.250.1-1.5) Changed to 1"
            })
            .Concat(Attempt("08:59:58", "09:00:00", "09:00:13", "339.1"))
            .ToArray());

        Assert.True(findings.Any);
        Assert.Equal("reset was pressed", findings.Waits[0].GaveUpBy);
    }
}
