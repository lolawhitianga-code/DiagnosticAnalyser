using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// The v1.1 review: is the report fair, does it establish normal before picking over the end, and
/// does it show the file rather than only its own reading of it.
/// <para>
/// Every line here comes from the real M22215 (SprintM600) export.
/// </para>
/// </summary>
public class FairAnalysisTests
{
    private static SpidaLogAnalysis Analyse(params string[] lines) =>
        new SpidaLogAnalyser().Analyse(
            MachineLogFile.Parse(lines), Array.Empty<ErrLogEntry>(), Array.Empty<ChangeLogEntry>(),
            new DateTime(2026, 9, 17, 9, 1, 0, DateTimeKind.Utc));

    /// <summary>
    /// A cut cycle, as that machine logs one. Four seconds, because an attempt shorter than two
    /// is treated as a sliver of the log rather than a real one.
    /// </summary>
    private static IEnumerable<string> Cut(string hhmmss, params string[] extra) => new[]
    {
        $"{hhmmss}.000,  Other, CutBoardPLC,  Step = 10",
        $"{hhmmss.Replace(":00", ":01")}.000,  Other, CutBoardPLC,  Step = 20",
        $"{hhmmss.Replace(":00", ":03")}.000,  Other, CutBoardPLC,  Step = 40",
        $"{hhmmss.Replace(":00", ":04")}.000,  Other, Machine,  BOARD Complete"
    }.Concat(extra);

    [Fact]
    public void TheRawTailIsEveryLineExactlyAsWritten()
    {
        // The defect this fixes: the summary kept only Other and MotionEvent, so on a real export
        // six of the last twenty lines were dropped - including the LidOpenRequest that caused the
        // servo disable 0.17s later. The report showed the disable with its cause removed.
        var analysis = Analyse(
            "09:00:00.762,  MotionEvent, Node0 Status,  OK",
            "09:00:13.819,  InputChange, LidOpenRequest,  Input (192.168.250.1-1.6) Changed to 1",
            "09:00:13.990,  MotionEvent, Node0 Status,  Servo Disabled",
            "09:00:14.479,  InputChange, LidClosed,  Input (192.168.250.1-0.3) Changed to 0",
            "09:00:23.169,  InputChange, ResetPB,  Input (192.168.250.1-1.5) Changed to 1");

        Assert.Equal(5, analysis.RawTail.Count);
        Assert.Contains(analysis.RawTail, e => e.Tag == "LidOpenRequest");
        Assert.Contains(analysis.RawTail, e => e.Tag == "ResetPB");

        // And in file order, unreordered.
        Assert.Equal(analysis.RawTail.Select(e => e.LineNumber).OrderBy(n => n),
            analysis.RawTail.Select(e => e.LineNumber));
    }

    [Fact]
    public void TheSummaryStillLeavesInputsOutButTheRawTailDoesNot()
    {
        var analysis = Analyse(
            "09:00:13.819,  InputChange, LidOpenRequest,  Input (192.168.250.1-1.6) Changed to 1",
            "09:00:13.990,  MotionEvent, Node0 Status,  Servo Disabled");

        Assert.DoesNotContain(analysis.FinalEntries, e => e.Tag == "LidOpenRequest");
        Assert.Contains(analysis.RawTail, e => e.Tag == "LidOpenRequest");
    }

    [Fact]
    public void AnOperatorPressingStopIsNotAMachineFault()
    {
        // "Stop All Pressed" turned up 29 times on a real export and headed the repeating-fault
        // list, under a sentence saying a repeat points at hardware. It points at a person.
        var analysis = Analyse(Cut("08:10:00")
            .Concat(new[] { "08:10:05.000,  Other, Machine,  Stop All Pressed" })
            .Concat(Cut("08:11:00"))
            .Concat(new[] { "08:11:05.000,  Other, Machine,  Stop All Pressed" })
            .ToArray());

        Assert.Empty(analysis.RepeatedFaults);

        var action = Assert.Single(analysis.OperatorActions);
        Assert.Equal("Stop All Pressed", action.Text);
        Assert.Equal(2, action.Occurrences);
    }

    [Fact]
    public void ADriveFaultOnAMotionEventIsFoundAtAll()
    {
        // The bug: FindFaults skipped every category except Other, so every drive-level fault was
        // invisible. Four Servo Movement Errors on a real export, two of them inside the window
        // the operator was complaining about, and the report never mentioned one.
        var analysis = Analyse(Cut("08:10:00",
            "08:10:02.000,  MotionEvent, Node0 Status,  Servo Movement Error").ToArray());

        var fault = Assert.Single(analysis.Cycles.SelectMany(c => c.MachineFaults));
        Assert.Contains("Servo Movement Error", fault.Text);
        Assert.Contains("Node0", fault.Text);
    }

    [Fact]
    public void AnAxisReportingItsOrdinaryStatesIsNotAFault()
    {
        var analysis = Analyse(Cut("08:10:00",
            "08:10:01.000,  MotionEvent, Node0 Status,  OK",
            "08:10:01.500,  MotionEvent, Node0 Status,  Moving",
            "08:10:02.000,  MotionEvent, Node0 Status,  Servo Disabled",
            "08:10:02.500,  MotionEvent, Node0 Status,  Axis Reset").ToArray());

        Assert.Empty(analysis.Cycles.SelectMany(c => c.MachineFaults));
    }

    [Fact]
    public void AnythingHappeningInMostAttemptsIsTheMachinesHabitNotAFault()
    {
        // "Unsafe to Move Axis" fires 370 times on a real export - every time the blade moves
        // during a cut. Treating it as a fault buried the four that mattered.
        var lines = new List<string>();
        for (var minute = 10; minute < 20; minute++)
        {
            lines.AddRange(Cut($"08:{minute:00}:00",
                $"08:{minute:00}:02.000,  Other, CutBoardPLC,  Timeout waiting for the usual thing"));
        }

        var analysis = Analyse(lines.ToArray());

        Assert.Empty(analysis.RepeatedFaults);
        Assert.Contains(analysis.Notes, n => n.Contains("more than half") && n.Contains("Timeout waiting"));
    }

    [Fact]
    public void ARareFaultSurvivesTheHabitRule()
    {
        var lines = new List<string>();
        for (var minute = 10; minute < 20; minute++) lines.AddRange(Cut($"08:{minute:00}:00"));

        lines.Add("08:15:02.000,  MotionEvent, Node0 Status,  Servo Movement Error");
        lines.Add("08:16:02.000,  MotionEvent, Node0 Status,  Servo Movement Error");

        var analysis = Analyse(lines.OrderBy(l => l).ToArray());

        var repeat = Assert.Single(analysis.RepeatedFaults);
        Assert.Contains("Servo Movement Error", repeat.Text);
    }

    [Fact]
    public void AnAdvisoryAboutHowTheJobIsSetUpIsNotAFault()
    {
        // "Outfeed clamps not used to prevent jamb" matched the fault wording on "jam" alone.
        var analysis = Analyse(Cut("08:10:00",
            "08:10:02.000,  Other, CSSPLC,  Outfeed clamps not used to prevent jamb").ToArray());

        Assert.Empty(analysis.Cycles.SelectMany(c => c.MachineFaults));
    }

    [Fact]
    public void NormalIsMeasuredBeforeTheEndIsPickedOver()
    {
        var lines = new List<string>();
        for (var minute = 10; minute < 20; minute++) lines.AddRange(Cut($"08:{minute:00}:00"));

        var analysis = Analyse(lines.ToArray());

        Assert.NotNull(analysis.Baseline);
        Assert.True(analysis.Baseline!.WasRunningNormally);
        Assert.Equal(10, analysis.Baseline.Attempted);
        Assert.True(analysis.Baseline.Typical > TimeSpan.Zero);
    }

    [Fact]
    public void TooFewCompletedAttemptsMeansNoBaselineIsClaimed()
    {
        // Claiming a typical cycle time from two attempts would be a number that looks measured
        // and is not.
        var analysis = Analyse(Cut("08:10:00").Concat(Cut("08:11:00")).ToArray());

        Assert.False(analysis.Baseline!.WasRunningNormally);
    }

    [Fact]
    public void TheLastAttemptIsComparedAgainstTheMachinesOwnHabit()
    {
        var lines = new List<string>();
        for (var minute = 10; minute < 20; minute++) lines.AddRange(Cut($"08:{minute:00}:00"));

        // A last attempt that starts and never finishes, four minutes long against a typical
        // fraction of a second.
        lines.Add("08:20:00.000,  Other, CutBoardPLC,  Step = 10");
        lines.Add("08:24:00.000,  Other, CutBoardPLC,  Step = 20");

        var analysis = Analyse(lines.ToArray());

        Assert.False(analysis.Baseline!.LastCompleted);
        Assert.True(analysis.Baseline.LastAgainstTypical > 3);
    }

    [Fact]
    public void AnEmptyLogDoesNotClaimABaseline()
    {
        var analysis = Analyse();

        Assert.Null(analysis.Baseline);
        Assert.Empty(analysis.RawTail);
    }
}
