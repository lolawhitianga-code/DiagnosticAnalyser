using DiagFileMonitor.Core.Production;
using DiagFileMonitor.Core.Reports;

namespace DiagFileMonitor.Core.Tests;

public class ProdLogParserTests
{
    [Fact]
    public void ReadsTheDocumentedLineShape()
    {
        var result = ProdLogParser.Parse(
            "PanelAssembled, 20260706 07:13:09, 0, 28, 0.139, 3, 8.3, 5, 20\r\n");

        var e = Assert.Single(result.Events);
        Assert.Equal(ProdLogEventKind.PanelAssembled, e.Kind);
        Assert.Equal(new DateTime(2026, 7, 6, 7, 13, 9), e.Timestamp);
        Assert.Equal("28", e.Field(1));
        Assert.Equal(0.139, e.Number(2), 3);
    }

    [Fact]
    public void SuppressesALineIdenticalToTheOneBeforeIt()
    {
        // Real exports repeat some events. On ten weeks of M21737 this was 49.6% of
        // MemberAssembled lines and 29.9% of MachineStopped.
        var result = ProdLogParser.Parse(string.Join("\r\n",
            "MemberAssembled, 20260907 05:21:09, 1, 0, F, 0.009, 2.325",
            "MemberAssembled, 20260907 05:21:09, 1, 0, F, 0.009, 2.325",
            "MemberAssembled, 20260907 05:21:10, 1, 0, F, 0.009, 2.325"));

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(1, result.ConsecutiveDuplicates);
    }

    [Fact]
    public void ADuplicateThatIsNotAdjacentIsKept()
    {
        // Only a line identical to the one immediately before it is a write-twice artefact.
        // The same event happening again later is a real second event.
        var result = ProdLogParser.Parse(string.Join("\r\n",
            "MachineStopped, 20260907 04:52:52",
            "MachineStarted, 20260907 05:20:05",
            "MachineStopped, 20260907 04:52:52"));

        Assert.Equal(3, result.Events.Count);
        Assert.Equal(0, result.ConsecutiveDuplicates);
    }

    [Fact]
    public void StripsNullPaddingAndSaysItWasThere()
    {
        var result = ProdLogParser.Parse("PanelStopped, 20260706 07:26:14, 29\0\0\r\n");

        Assert.True(result.HadNullPadding);
        Assert.Single(result.Events);
    }

    [Fact]
    public void OneBadLineDoesNotAbortTheFile()
    {
        var result = ProdLogParser.Parse(string.Join("\r\n",
            "PanelStarted, 20260907 04:50:22, 73",
            "this is not a log line at all",
            "PanelStarted, not-a-timestamp, 74",
            "PanelStopped, 20260907 04:52:52, 73"));

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(2, result.MalformedLines);
    }

    [Fact]
    public void AnUnrecognisedEventIsCountedNotDropped()
    {
        var result = ProdLogParser.Parse("SomethingNew, 20260907 04:50:22, 1");

        Assert.Equal(ProdLogEventKind.Unknown, result.Events[0].Kind);
        Assert.Equal(1, result.UnknownEventNames["SomethingNew"]);
    }

    [Theory]
    [InlineData("ProdLogV22026W37.log", 2026, 37)]
    [InlineData("ProdLogV22026W07.log", 2026, 7)]
    [InlineData("prodlogv22025w52.LOG", 2025, 52)]
    public void ReadsTheYearAndWeekFromTheFileName(string name, int year, int week)
    {
        Assert.Equal((year, week), ProdLogParser.WeekFromFileName(name));
    }

    [Theory]
    [InlineData("MachineLog.txt")]
    [InlineData("ErrLog.txt")]
    [InlineData("ProdLog.log")]
    public void LeavesFilesThatAreNotProductionLogsAlone(string name)
    {
        Assert.Null(ProdLogParser.WeekFromFileName(name));
        Assert.False(ProdLogParser.LooksLikeProdLog(name));
    }
}

public class PanelClassifierTests
{
    private static IReadOnlyList<PanelRecord> Classify(params string[] lines) =>
        new PanelClassifier().Classify(ProdLogParser.Parse(string.Join("\r\n", lines)).Events).Panels;

    [Fact]
    public void APanelWithWorkInItIsCompleted()
    {
        var panels = Classify(
            "PanelStarted, 20260706 07:10:00, 28",
            "MemberAssembled, 20260706 07:11:00, 1, 0, F, 0.009, 2.325",
            "PanelAssembled, 20260706 07:13:09, 8, 28, 0.139, 3, 8.3, 5, 20");

        var panel = Assert.Single(panels);
        Assert.Equal(PanelOutcome.Completed, panel.Outcome);
        Assert.Equal(8, panel.FastenerCount);
        Assert.Equal(1, panel.MembersAssembled);
        Assert.Equal(0.139, panel.Cube, 3);
        Assert.Equal(8.3, panel.BuildMinutes, 2);
    }

    [Fact]
    public void AnEmptyAssemblyIsSteppedPastNotAFault()
    {
        // The operator advancing the HMI past a panel that does not need building. On the M21737
        // sample this was 1,002 of 5,667 assembled panels - counting it as a fault would drown
        // out the real ones.
        var panels = Classify(
            "PanelStarted, 20260706 07:10:00, 28",
            "PanelAssembled, 20260706 07:10:02, 0, 28, 0, 0, 0, 0, 0");

        Assert.Equal(PanelOutcome.SteppedPast, Assert.Single(panels).Outcome);
    }

    [Fact]
    public void ARestartUnderTheSameNameIsNotANewPanel()
    {
        // The HMI re-issues PanelStarted for the panel already open. 15.3% of starts on the
        // real sample.
        var classified = new PanelClassifier().Classify(ProdLogParser.Parse(string.Join("\r\n",
            "PanelStarted, 20260706 07:10:00, 28",
            "PanelStarted, 20260706 07:11:00, 28",
            "MemberAssembled, 20260706 07:11:30, 1, 0, F, 0.009, 2.325",
            "PanelAssembled, 20260706 07:13:09, 8, 28, 0.139, 3, 2.1, 0, 20")).Events);

        Assert.Equal(1, classified.SameNameRestarts);
        Assert.Equal(PanelOutcome.Completed, Assert.Single(classified.Panels).Outcome);
    }

    [Fact]
    public void ARestartResetsTheBuildClockRatherThanTheMemberCount()
    {
        // Build minutes are measured from the LAST start, so the restart time is what is kept -
        // but the members already assembled are still part of the panel.
        var panels = Classify(
            "PanelStarted, 20260706 07:10:00, 28",
            "MemberAssembled, 20260706 07:10:30, 1, 0, F, 0.009, 2.325",
            "PanelStarted, 20260706 07:11:00, 28",
            "PanelAssembled, 20260706 07:13:09, 8, 28, 0.139, 3, 2.1, 0, 20");

        var panel = Assert.Single(panels);
        Assert.Equal(new DateTime(2026, 7, 6, 7, 11, 0), panel.StartedAt);
        Assert.Equal(1, panel.MembersAssembled);
    }

    [Fact]
    public void APanelLeftOpenByADifferentNameIsSupersededNotAFault()
    {
        // Panel names are reused labels, not unique numbers - "E5" started 139 times and
        // assembled 53 times on the real sample. Calling every unclosed start an abandoned panel
        // gives a 37.7% fault rate, which is operators moving around the HMI.
        var panels = Classify(
            "PanelStarted, 20260706 07:10:00, 28",
            "PanelStarted, 20260706 07:10:30, 29",
            "PanelAssembled, 20260706 07:13:09, 8, 29, 0.139, 3, 2.1, 0, 20");

        Assert.Equal(2, panels.Count);
        Assert.Equal(PanelOutcome.Superseded, panels[0].Outcome);
        Assert.Equal(PanelOutcome.Completed, panels[1].Outcome);
    }

    [Fact]
    public void PanelStoppedIsTheRealAbandonmentSignal()
    {
        var panels = Classify(
            "PanelStarted, 20260706 07:10:00, 28",
            "MemberAssembled, 20260706 07:11:00, 1, 0, F, 0.009, 2.325",
            "PanelStopped, 20260706 07:12:00, 28");

        Assert.Equal(PanelOutcome.StoppedByOperator, Assert.Single(panels).Outcome);
    }

    [Fact]
    public void AnImplausibleBuildTimeIsFlaggedButThePanelStillCounts()
    {
        // A missing stop event, not a panel that really took five hours. Seen at 304 minutes on
        // the real sample.
        var panel = Assert.Single(Classify(
            "PanelStarted, 20260706 07:10:00, 28",
            "MemberAssembled, 20260706 07:11:00, 1, 0, F, 0.009, 2.325",
            "PanelAssembled, 20260706 12:14:00, 8, 28, 0.139, 3, 304.3, 0, 20"));

        Assert.True(panel.BuildTimeImplausible);
        Assert.Equal(PanelOutcome.Completed, panel.Outcome);
    }

    [Fact]
    public void APanelStillOpenWhenTheLogEndsIsNotRecordedAsAnything()
    {
        // The week ran out. That is not evidence of an abandoned panel.
        Assert.Empty(Classify("PanelStarted, 20260706 07:10:00, 28"));
    }
}

public class ProductionAnalyserTests
{
    private static PanelRecord Panel(string day, int hour, int minute, PanelOutcome outcome, double cube = 0.1) =>
        new()
        {
            Name = "P",
            EndedAt = DateTime.Parse($"{day}T{hour:00}:{minute:00}:00"),
            Outcome = outcome,
            Cube = cube,
            Lineal = cube * 20
        };

    [Fact]
    public void ADayWithNoOutputStillGetsARow()
    {
        // A day where this machine sat at zero while the site worked is the most useful thing
        // this data shows. Dropping empty days would hide it.
        var summary = ProductionAnalyser.Summarise(new[]
        {
            Panel("2026-07-06", 8, 0, PanelOutcome.Completed),
            Panel("2026-07-09", 8, 0, PanelOutcome.Completed)
        }, ShiftModel.SingleDayShift);

        Assert.Equal(4, summary.CalendarDays);
        Assert.Equal(2, summary.DaysWithOutput);
        Assert.Equal(2, summary.Days.Count(d => !d.HadOutput));
    }

    [Fact]
    public void AnEmptyDayIsNotCountedAsPlannedTime()
    {
        // Otherwise a shutdown nobody was rostered for drags availability down as though the
        // machine had been standing idle on shift.
        var summary = ProductionAnalyser.Summarise(new[]
        {
            Panel("2026-07-06", 8, 0, PanelOutcome.Completed),
            Panel("2026-07-08", 8, 0, PanelOutcome.Completed)
        }, ShiftModel.SingleDayShift);

        Assert.Equal(0, summary.Days.Single(d => d.Day.Day == 7).PlannedMinutes);
    }

    [Fact]
    public void SupersededPanelsStayOutOfTheFaultRate()
    {
        var summary = ProductionAnalyser.Summarise(new[]
        {
            Panel("2026-07-06", 8, 0, PanelOutcome.Completed),
            Panel("2026-07-06", 8, 5, PanelOutcome.Completed),
            Panel("2026-07-06", 8, 6, PanelOutcome.Superseded),
            Panel("2026-07-06", 8, 7, PanelOutcome.Superseded),
            Panel("2026-07-06", 8, 8, PanelOutcome.StoppedByOperator)
        }, ShiftModel.SingleDayShift);

        // 1 stop out of 3 closed panels, not 1 out of 5.
        Assert.Equal(1.0 / 3, summary.FaultRate!.Value, 4);
        Assert.Equal(2, summary.Superseded);
    }

    [Fact]
    public void AGapInsideAScheduledBreakIsNotAnUnplannedStop()
    {
        var summary = ProductionAnalyser.Summarise(new[]
        {
            Panel("2026-07-06", 12, 35, PanelOutcome.Completed),   // inside lunch 12:30-13:00
            Panel("2026-07-06", 13, 10, PanelOutcome.Completed)
        }, ShiftModel.SingleDayShift);

        Assert.Equal(0, summary.Days.Single().UnplannedStopMinutes);
    }

    [Fact]
    public void AGapOutsideABreakIsAnUnplannedStop()
    {
        var summary = ProductionAnalyser.Summarise(new[]
        {
            Panel("2026-07-06", 8, 0, PanelOutcome.Completed),
            Panel("2026-07-06", 9, 0, PanelOutcome.Completed)
        }, ShiftModel.SingleDayShift);

        Assert.Equal(60, summary.Days.Single().UnplannedStopMinutes);
    }

    [Fact]
    public void AGapLongerThanTheCeilingIsCappedRatherThanCountedWhole()
    {
        // A log gap is unknown time, not measured downtime. Capping it stops a quiet stretch
        // reading as if the machine had been standing idle the whole way through.
        var model = ShiftModel.SingleDayShift;

        var summary = ProductionAnalyser.Summarise(new[]
        {
            new PanelRecord { EndedAt = new DateTime(2026, 7, 6, 0, 30, 0), Outcome = PanelOutcome.Completed },
            new PanelRecord { EndedAt = new DateTime(2026, 7, 6, 23, 30, 0), Outcome = PanelOutcome.Completed }
        }, model);

        Assert.Equal(model.MaxGapMinutes, summary.Days.Single().UnplannedStopMinutes);
    }

    [Fact]
    public void AGapAcrossDaysIsNeverCountedAsLostProduction()
    {
        // Gaps are measured inside a day. A weekend shows up as days with no output and no
        // planned time, which is the honest answer - nobody was rostered.
        var summary = ProductionAnalyser.Summarise(new[]
        {
            Panel("2026-07-03", 16, 0, PanelOutcome.Completed),
            Panel("2026-07-06", 8, 0, PanelOutcome.Completed)
        }, ShiftModel.SingleDayShift);

        Assert.All(summary.Days, d => Assert.Equal(0, d.UnplannedStopMinutes));
        Assert.Equal(2, summary.Days.Count(d => d.PlannedMinutes == 0));
    }

    [Fact]
    public void FindsADayOneMachineSatOutWhileTheOtherWorked()
    {
        var quiet = ProductionAnalyser.Summarise(
            Enumerable.Range(1, 10).SelectMany(d => d == 5
                    ? Array.Empty<PanelRecord>()
                    : Enumerable.Range(0, 50).Select(i => Panel($"2026-07-{d:00}", 8, i, PanelOutcome.Completed)).ToArray())
                .ToList(), ShiftModel.SingleDayShift);

        var busy = ProductionAnalyser.Summarise(
            Enumerable.Range(1, 10).SelectMany(d =>
                    Enumerable.Range(0, 50).Select(i => Panel($"2026-07-{d:00}", 8, i, PanelOutcome.Completed)))
                .ToList(), ShiftModel.SingleDayShift);

        var down = ProductionAnalyser.LikelyUnplannedDowntime(quiet, busy);

        Assert.Equal(new DateOnly(2026, 7, 5), Assert.Single(down));
    }
}

public class ProductionReportTests
{
    private static ProductionSummary Summary()
    {
        var panels = new List<PanelRecord>();
        for (var day = 6; day <= 10; day++)
        for (var i = 0; i < 20; i++)
        {
            panels.Add(new PanelRecord
            {
                Name = "P",
                EndedAt = new DateTime(2026, 7, day, 8, i * 2, 0),
                Outcome = PanelOutcome.Completed,
                Cube = 0.15,
                Lineal = 3,
                BuildMinutes = 2
            });
        }

        panels.Add(new PanelRecord
        {
            EndedAt = new DateTime(2026, 7, 6, 9, 0, 0), Outcome = PanelOutcome.Superseded
        });

        return ProductionAnalyser.Summarise(panels, ShiftModel.SingleDayShift, "M21737", "PlaceMakers Auckland");
    }

    [Fact]
    public void TheShiftModelIsPrintedBecauseAvailabilityRestsOnIt()
    {
        var html = new ReportHtmlRenderer().Render(ProductionReport.Build(Summary(), "4.8M Raked Extruder"));

        Assert.Contains("This is an assumption:", html);
        Assert.Contains("Single day shift", html);
        Assert.Contains("Lunch", html);
        Assert.Contains("only comparable on this figure if they are on the same model", html);
    }

    [Fact]
    public void TheSupersededCountIsExplainedRatherThanLeftLookingAlarming()
    {
        var html = new ReportHtmlRenderer().Render(ProductionReport.Build(Summary()));

        Assert.Contains("Not counted as faults:", html);
        Assert.Contains("reused labels", html);
    }

    [Fact]
    public void TheUnverifiedFastenerColumnIsFlagged()
    {
        var html = new ReportHtmlRenderer().Render(ProductionReport.Build(Summary()));

        Assert.Contains("Not yet verified:", html);
        Assert.Contains("fastener count", html);
    }

    [Fact]
    public void TheReportIsStillSelfContained()
    {
        var html = new ReportHtmlRenderer().Render(ProductionReport.Build(Summary()));

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
    }
}
