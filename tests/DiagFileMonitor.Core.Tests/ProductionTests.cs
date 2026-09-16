using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;
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
    public void TimeSpentIsEnoughToNotBeSteppedPast()
    {
        // Stepped past means the operator advanced without the machine doing anything. Build time
        // on the clock means it did something, whatever the fastener counter says.
        var panels = Classify("PanelAssembled, 20260706 07:12:00, 0, 28, 0.05, 1.2, 1.9, 0, 8");

        Assert.NotEqual(PanelOutcome.SteppedPast, Assert.Single(panels).Outcome);
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
    public void PanelStoppedMarksTheCompletionItKilled()
    {
        // The controller writes PanelStopped and then still writes a PanelAssembled for the same
        // panel a moment later. The stop marks that completion rather than standing beside it.
        var panels = Classify(
            "PanelStarted, 20260706 07:10:00, 28",
            "MemberAssembled, 20260706 07:11:00, 1, 0, F, 0.009, 2.325",
            "PanelStopped, 20260706 07:12:00, 28",
            "PanelAssembled, 20260706 07:12:04, 0, 28, 0.05, 1.2, 1.9, 0, 8");

        Assert.Equal(PanelOutcome.StoppedByOperator, Assert.Single(panels).Outcome);
    }

    [Fact]
    public void AStopThatMatchesNoCompletionIsDropped()
    {
        // On its own a stop says nothing - the panel it refers to never closed, and an unclosed
        // panel is not evidence of anything.
        var panels = Classify(
            "PanelStarted, 20260706 07:10:00, 28",
            "PanelStopped, 20260706 07:12:00, 28");

        Assert.Empty(panels.Where(p => p.Outcome == PanelOutcome.StoppedByOperator));
    }

    [Fact]
    public void AStopLongAfterACompletionIsNotThatPanel()
    {
        var panels = Classify(
            "PanelAssembled, 20260706 07:12:00, 8, 28, 0.139, 3, 2.8, 1, 20",
            "PanelStopped, 20260706 07:30:00, 28");

        Assert.Equal(PanelOutcome.Completed, Assert.Single(panels).Outcome);
    }

    [Fact]
    public void TimeSpentWithNothingFiredIsAFaultWhereTheCounterWasWorking()
    {
        // Another panel that day fired, so the counter was demonstrably alive and a zero means
        // the machine really did run and fire nothing.
        var panels = Classify(
            "PanelAssembled, 20260706 07:12:00, 8, A, 0.139, 3, 2.8, 1, 20",
            "PanelAssembled, 20260706 07:20:00, 0, B, 0.100, 2, 3.1, 1, 16");

        Assert.Equal(PanelOutcome.Completed, panels[0].Outcome);
        Assert.Equal(PanelOutcome.RanButNailedNothing, panels[1].Outcome);
    }

    [Fact]
    public void OnADayTheCounterWasOffAZeroSaysNothing()
    {
        // No panel that day reports a single fastener despite real build time, so the counter was
        // off and build time alone decides. Without this rule a whole month of real production
        // reads as faults.
        var panels = Classify(
            "PanelAssembled, 20260706 07:12:00, 0, A, 0.139, 3, 2.8, 1, 20",
            "PanelAssembled, 20260706 07:20:00, 0, B, 0.100, 2, 3.1, 1, 16");

        Assert.All(panels, p => Assert.Equal(PanelOutcome.Completed, p.Outcome));
        Assert.All(panels, p => Assert.False(p.FastenerCounterLive));
    }

    [Fact]
    public void TheCounterIsJudgedPerDayNotAcrossTheWholeLog()
    {
        var panels = Classify(
            "PanelAssembled, 20260706 07:12:00, 8, A, 0.139, 3, 2.8, 1, 20",
            "PanelAssembled, 20260707 07:20:00, 0, B, 0.100, 2, 3.1, 1, 16");

        Assert.Equal(PanelOutcome.Completed, panels[0].Outcome);
        // Different day, no fastener seen on it - so the zero is not held against it.
        Assert.Equal(PanelOutcome.Completed, panels[1].Outcome);
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

        // Off-shift and break minutes come out of the gap first, so what is left is the rostered
        // part of the day - which is less than the ceiling here.
        var day = summary.Days.Single();
        Assert.True(day.UnplannedStopMinutes <= model.MaxGapMinutes);
        Assert.True(day.UnplannedStopMinutes > 0);
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

public class ProductionSerialTests
{
    [Theory]
    [InlineData(@"D:\Production\M21737", "M21737")]
    [InlineData(@"D:\Production\M21737\Reports", "M21737")]
    [InlineData(@"D:\Production\AOR1694\SDN\Reports", "AOR1694")]
    [InlineData(@"D:\logs\AOR1613 Carters Line 3", "AOR1613")]
    [InlineData(@"D:\logs\DGM20771 TrussTech", "DGM20771")]
    [InlineData(@"D:\logs\M21642-1", "M21642-1")]
    [InlineData("/mnt/exports/m20716/reports", "M20716")]
    public void ReadsTheSerialOutOfAFolderName(string path, string expected)
    {
        Assert.Equal(expected, ProductionSerial.FromPath(path));
    }

    [Fact]
    public void TheDeepestFolderWithASerialWins()
    {
        // A parent folder can carry a serial too. The one closest to the files is the right answer.
        Assert.Equal("M21844", ProductionSerial.FromPath(@"D:\M21737 old\M21844\Reports"));
    }

    [Theory]
    [InlineData(@"D:\Production\Reports")]
    [InlineData(@"D:\Production\Line 3")]
    [InlineData(@"D:\Production\2026 exports")]
    [InlineData("")]
    [InlineData(null)]
    public void SaysNothingRatherThanGuessing(string? path)
    {
        Assert.Null(ProductionSerial.FromPath(path));
    }

    [Theory]
    [InlineData(@"D:\Machines\TornadoM450\Reports")]
    [InlineData(@"D:\Machines\SprintM600")]
    [InlineData(@"D:\Machines\TornadoM500 logs")]
    public void AModelNameIsNotMistakenForASerial(string path)
    {
        // M450, M600 and M500 are machine types. Filing a machine's production under its model
        // name would merge every machine of that type into one set of figures.
        Assert.Null(ProductionSerial.FromPath(path));
    }
}

public class MachineFolderSweepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sweeptests", Guid.NewGuid().ToString("N"));

    public MachineFolderSweepTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void Machine(string folder, params string[] weeks)
    {
        var path = Path.Combine(_root, folder);
        Directory.CreateDirectory(path);

        foreach (var week in weeks)
        {
            File.WriteAllText(Path.Combine(path, week),
                "PanelAssembled, 20260706 07:13:09, 8, 28, 0.139, 3, 8.3, 5, 20\r\n");
        }
    }

    [Fact]
    public void FindsEachMachineFolderAndItsSerial()
    {
        Machine("M21737", "ProdLogV22026W28.log");
        Machine("AOR1694 Carters Line 3", "ProdLogV22026W29.log");

        var found = ProductionSerial.MachineFolders(_root);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, f => f.Serial == "M21737");
        Assert.Contains(found, f => f.Serial == "AOR1694");
    }

    [Fact]
    public void AFolderWithNoProductionLogsIsNotAMachine()
    {
        Machine("M21737", "ProdLogV22026W28.log");
        Directory.CreateDirectory(Path.Combine(_root, "M21844 empty"));
        File.WriteAllText(Path.Combine(_root, "M21844 empty", "notes.txt"), "nothing here");

        Assert.Equal("M21737", Assert.Single(ProductionSerial.MachineFolders(_root)).Serial);
    }

    [Fact]
    public void AMachineFolderWithNoSerialInItsNameIsReportedNotGuessedAt()
    {
        Machine("Line 3", "ProdLogV22026W28.log");

        var found = Assert.Single(ProductionSerial.MachineFolders(_root));
        Assert.Null(found.Serial);
    }

    [Fact]
    public async Task SweepingAParentFolderFilesEachMachineUnderItsOwnSerial()
    {
        Machine("M21737", "ProdLogV22026W28.log");
        Machine("AOR1694", "ProdLogV22026W29.log");
        Machine("Line 3 no serial", "ProdLogV22026W30.log");

        using var env = new TestEnvironment();
        var import = new ProductionImportService(env.CreateContext);

        var result = await import.ImportMachineFoldersAsync(_root);

        Assert.Equal(2, result.FilesRead);
        Assert.Equal(new[] { "AOR1694", "M21737" }, result.Serials.OrderBy(s => s).ToArray());

        // The unnamed folder is left alone and said out loud, not filed under a guess.
        Assert.Contains(result.Notes, n => n.Contains("no serial number in the folder name"));

        var stored = await import.StoredMachinesAsync();
        Assert.Equal(2, stored.Count);
    }
}

/// <summary>
/// A support bundle carries its own production data as Reports/LatestReport.txt - same format as
/// the weekly exports, nothing in the name to say so, and covering the few days before the bundle
/// was taken. Unlike a weekly export it also carries a Machine.xml, so it is the one source that
/// says which machine the production belongs to without anybody typing it in.
/// </summary>
public class BundleProductionTests
{
    private const string ProductionLines = """
        PanelStarted, 20260727 10:43:11, E-1D
        MachineStarted, 20260727 10:43:11
        MemberAssembled, 20260727 10:45:18, 1, 0, Q, 0.01, 2.381
        MemberAssembled, 20260727 10:45:18, 1, 0, Q, 0.01, 2.381
        PanelAssembled, 20260727 10:46:00, 8, E-1D, 0.139, 3, 2.8, 1, 20
        PanelStarted, 20260727 10:47:19, E-2D
        MemberAssembled, 20260727 10:48:18, 1, 0, D, 0.012, 3.078
        PanelAssembled, 20260727 10:49:30, 12, E-2D, 0.105, 2.15, 2.2, 0.5, 16
        UserLogout, 20260728 00:19:30, Spida
        """;

    private static Dictionary<string, string> Bundle(string serial, string? report) 
    {
        var files = new Dictionary<string, string>
        {
            ["Machine.xml"] = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <Machine><Title>Spida SDN, V2.4.0.0, {serial}, Carters, RakingWallExtruderV3DG</Title></Machine>
                """,
            ["Logs/MachineLog.txt"] = "07:53:30.1747290,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On\n"
        };

        if (report is not null) files["Reports/LatestReport.txt"] = report;
        return files;
    }

    [Fact]
    public void ProductionContentIsRecognisedByWhatIsInItNotWhatItIsCalled()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, ProductionLines);
            Assert.True(ProdLogParser.LooksLikeProductionContent(path));

            File.WriteAllText(path, "07:53:30.1747290,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On\n");
            Assert.False(ProdLogParser.LooksLikeProductionContent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UserLogoutIsARealEventNotAnUnknownOne()
    {
        // Not in the reference guide's list, but it turns up in a real bundle's production report.
        var result = ProdLogParser.Parse("UserLogout, 20260728 00:19:30, Spida");

        Assert.Equal(ProdLogEventKind.UserLogout, Assert.Single(result.Events).Kind);
        Assert.Empty(result.UnknownEventNames);
    }

    [Fact]
    public async Task ProcessingABundleFilesItsProductionUnderTheMachineTheBundleReported()
    {
        using var env = new TestEnvironment();

        var file = await env.Processor.ProcessAsync(
            env.CreateZip("M20716SupportFile.szip", Bundle("M20716", ProductionLines)));

        Assert.Equal("M20716", file.SerialNumber);

        var stored = Assert.Single(await env.Production.StoredMachinesAsync());
        Assert.Equal("M20716", stored.SerialNumber);
        Assert.Equal(2, stored.Panels);

        var panels = await env.Production.LoadPanelsAsync("M20716");
        Assert.All(panels, p => Assert.Equal(PanelOutcome.Completed, p.Outcome));
        Assert.Equal(0.244, panels.Sum(p => p.Cube), 3);
    }

    [Fact]
    public async Task TheWeekComesFromTheEventsBecauseTheNameCarriesNone()
    {
        using var env = new TestEnvironment();
        await env.Processor.ProcessAsync(env.CreateZip("M20716SupportFile.szip", Bundle("M20716", ProductionLines)));

        await using var context = env.CreateContext();
        var record = context.ProductionLogFiles.Single();

        // 28 July 2026 is ISO week 31.
        Assert.Equal(2026, record.Year);
        Assert.Equal(31, record.Week);
        Assert.Equal(ProductionSources.SupportBundle, record.Source);
        Assert.Equal(new DateTime(2026, 7, 27, 10, 46, 0), record.CoversFromUtc);
    }

    [Fact]
    public async Task AnEmptyReportIsSkippedWithoutComplaint()
    {
        // The real AOR1613 bundle carries a zero byte LatestReport.txt.
        using var env = new TestEnvironment();

        var file = await env.Processor.ProcessAsync(
            env.CreateZip("AOR1613SupportFiles.szip", Bundle("AOR1613", string.Empty)));

        Assert.Equal(ProcessingStatus.Processed, file.Status);
        Assert.Empty(await env.Production.StoredMachinesAsync());
    }

    [Fact]
    public async Task ABundleWithNoProductionReportIsStillProcessed()
    {
        using var env = new TestEnvironment();

        var file = await env.Processor.ProcessAsync(
            env.CreateZip("M20716SupportFile.szip", Bundle("M20716", report: null)));

        Assert.Equal(ProcessingStatus.Processed, file.Status);
        Assert.Empty(await env.Production.StoredMachinesAsync());
    }

    [Fact]
    public async Task TheSameBundleTwiceDoesNotDoubleTheProduction()
    {
        using var env = new TestEnvironment();

        await env.Processor.ProcessAsync(env.CreateZip("a.szip", Bundle("M20716", ProductionLines)));
        await env.Processor.ProcessAsync(env.CreateZip("b.szip", Bundle("M20716", ProductionLines)));

        Assert.Equal(2, Assert.Single(await env.Production.StoredMachinesAsync()).Panels);
    }

    [Fact]
    public async Task AWeeklyExportOverlappingABundleCountsEachPanelOnce()
    {
        // The two sources overlap by design: a bundle holds the days before it was taken, and the
        // weekly export for that week arrives later holding the same days. Counting both would
        // inflate every figure built on them.
        using var env = new TestEnvironment();

        await env.Processor.ProcessAsync(env.CreateZip("a.szip", Bundle("M20716", ProductionLines)));

        var weekly = Path.Combine(env.RootPath, "ProdLogV22026W31.log");
        File.WriteAllText(weekly, ProductionLines);

        var result = await env.Production.ImportFilesAsync(new[] { weekly }, "M20716");

        Assert.Equal(1, result.FilesRead);
        Assert.Equal(0, result.PanelsStored);
        Assert.Contains(result.Notes, n => n.Contains("already held from another source"));

        // Both files are recorded, but the panels are counted once.
        Assert.Equal(2, Assert.Single(await env.Production.StoredMachinesAsync()).Weeks);
        Assert.Equal(2, (await env.Production.LoadPanelsAsync("M20716")).Count);
    }
}

/// <summary>
/// The layout production logs actually arrive in: a top folder, one folder per machine named with
/// its serial, and the unpacked bundle under each - so the logs sit several levels down in
/// <c>&lt;machine&gt;\SDN\Reports</c>.
/// </summary>
public class RawLogsFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rawlogs", Guid.NewGuid().ToString("N"));

    private const string OneWeek = """
        PanelStarted, 20260706 07:10:00, E-1D
        MemberAssembled, 20260706 07:11:00, 1, 0, Q, 0.01, 2.381
        PanelAssembled, 20260706 07:13:09, 8, E-1D, 0.139, 3, 2.8, 1, 20
        """;

    public RawLogsFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Reports(string machineFolder)
    {
        var path = Path.Combine(_root, machineFolder, "SDN", "Reports");
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void AMachineIsFoundThroughTheUnpackedBundleBelowIt()
    {
        // The logs are three levels down, and the folder is named with more than the serial.
        File.WriteAllText(Path.Combine(Reports("M21737 raked extruder 4.8"), "ProdLogV22026W28.log"), OneWeek);

        var found = Assert.Single(ProductionSerial.MachineFolders(_root));
        Assert.Equal("M21737", found.Serial);
    }

    [Fact]
    public async Task EachMachineFolderIsReadUnderItsOwnSerial()
    {
        File.WriteAllText(Path.Combine(Reports("M21737 raked extruder 4.8"), "ProdLogV22026W28.log"), OneWeek);
        File.WriteAllText(Path.Combine(Reports("AOR1694 line 3"), "ProdLogV22026W30.log"), OneWeek);

        using var env = new TestEnvironment();
        var result = await new ProductionImportService(env.CreateContext).ImportMachineFoldersAsync(_root);

        Assert.Equal(2, result.FilesRead);
        Assert.Equal(new[] { "AOR1694", "M21737" }, result.Serials.OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task AnEmptyWeekIsNotStoredAsAWeekWithNoProduction()
    {
        // A zero byte log is a file with nothing in it, not a week the machine sat idle. Storing
        // it would put a phantom shutdown in the machine's history.
        var reports = Reports("M21737");
        File.WriteAllText(Path.Combine(reports, "ProdLogV22026W28.log"), OneWeek);
        File.WriteAllText(Path.Combine(reports, "ProdLogV22020W06.log"), string.Empty);

        using var env = new TestEnvironment();
        var import = new ProductionImportService(env.CreateContext);
        var result = await import.ImportMachineFoldersAsync(_root);

        Assert.Equal(1, result.FilesRead);
        Assert.Equal(1, result.FilesEmpty);
        Assert.Contains(result.Notes, n => n.Contains("empty"));
        Assert.Equal(1, Assert.Single(await import.StoredMachinesAsync()).Weeks);
    }

    [Fact]
    public async Task TheOlderProdLogNamingIsReportedRatherThanLumpedInWithTheShiftLogs()
    {
        // ProdLog2020W07.log is the same naming without the V2. It is recognised so it can be
        // asked about, but not read - nobody has supplied one with data in it.
        var reports = Reports("M21737");
        File.WriteAllText(Path.Combine(reports, "ProdLogV22026W28.log"), OneWeek);
        File.WriteAllText(Path.Combine(reports, "ProdLog2020W07.log"), OneWeek);
        File.WriteAllText(Path.Combine(reports, "ShiftLog2019W28.log"), "something else");

        using var env = new TestEnvironment();
        var result = await new ProductionImportService(env.CreateContext).ImportMachineFoldersAsync(_root);

        Assert.Equal(1, result.FilesRead);
        Assert.Equal(1, result.FilesInOlderFormat);
        Assert.Equal(1, result.FilesSkippedNotProdLog);
        Assert.Contains(result.Notes, n => n.Contains("older way"));
    }

    [Theory]
    [InlineData("ProdLog2020W07.log", true)]
    [InlineData("ProdLog2026W7.log", true)]
    [InlineData("ProdLogV22026W37.log", false)]   // the current naming is read, not flagged
    [InlineData("ShiftLog2019W28.log", false)]
    [InlineData("LatestReport.txt", false)]
    public void TheOlderNamingIsToldApartFromEverythingElse(string name, bool older)
    {
        Assert.Equal(older, ProdLogParser.LooksLikeOlderProdLog(name));
    }

    [Fact]
    public void AFolderWithNoProductionLogsIsNotAMachine()
    {
        File.WriteAllText(Path.Combine(Reports("M21737"), "ProdLogV22026W28.log"), OneWeek);
        Directory.CreateDirectory(Path.Combine(_root, "Notes only"));
        File.WriteAllText(Path.Combine(_root, "Notes only", "readme.txt"), "nothing here");

        Assert.Equal("M21737", Assert.Single(ProductionSerial.MachineFolders(_root)).Serial);
    }

    [Fact]
    public async Task ReadingTheSameRootTwiceDoesNotDoubleAnything()
    {
        File.WriteAllText(Path.Combine(Reports("M21737"), "ProdLogV22026W28.log"), OneWeek);

        using var env = new TestEnvironment();
        var import = new ProductionImportService(env.CreateContext);

        await import.ImportMachineFoldersAsync(_root);
        var again = await import.ImportMachineFoldersAsync(_root);

        Assert.Equal(0, again.FilesRead);
        Assert.Equal(1, again.FilesSkippedAlreadyStored);
        Assert.Equal(1, Assert.Single(await import.StoredMachinesAsync()).Panels);
    }
}

/// <summary>
/// Availability. These pin the corrections found by reading the delivered DGM20771 report, where
/// our own version was measurably wrong.
/// </summary>
public class AvailabilityTests
{
    private static ShiftModel Shift => new()
    {
        Name = "test",
        ShiftStart = new TimeOnly(7, 0),
        ShiftEnd = new TimeOnly(17, 0),
        UnplannedStopMinutes = 20,
        Breaks = new[] { new ShiftBreak("Lunch", new TimeOnly(12, 30), new TimeOnly(13, 0)) }
    };

    private static PanelRecord At(int hour, int minute) => new()
    {
        EndedAt = new DateTime(2026, 7, 6, hour, minute, 0), Outcome = PanelOutcome.Completed
    };

    [Fact]
    public void ALongStoppageStartingAtLunchIsNotExcusedByLunch()
    {
        // The defect this fixes: a gap was discounted in full if it merely began during a break,
        // so a three hour stoppage that started at 12:35 counted as nothing at all.
        var summary = ProductionAnalyser.Summarise(new[] { At(12, 35), At(15, 35) }, Shift);

        // Three hours, less the 25 minutes of lunch inside it.
        Assert.Equal(155, summary.Days.Single().UnplannedStopMinutes, 0);
    }

    [Fact]
    public void LunchItselfIsNotAStoppage()
    {
        var summary = ProductionAnalyser.Summarise(new[] { At(12, 25), At(13, 5) }, Shift);

        // Forty minutes of clock, thirty of it lunch - ten left, under the threshold.
        Assert.Equal(0, summary.Days.Single().UnplannedStopMinutes);
    }

    [Fact]
    public void TheWaitBeforeTheFirstPanelCountsAgainstTheDay()
    {
        var summary = ProductionAnalyser.Summarise(new[] { At(9, 0), At(9, 10) }, Shift);

        Assert.Equal(120, summary.Days.Single().StartupMinutes, 0);
    }

    [Fact]
    public void TheWaitAfterTheLastPanelCountsToo()
    {
        var summary = ProductionAnalyser.Summarise(new[] { At(7, 10), At(15, 0) }, Shift);

        Assert.Equal(120, summary.Days.Single().TailMinutes, 0);
    }

    [Fact]
    public void ADayRunEndToEndIsFullyAvailable()
    {
        var panels = new List<PanelRecord>();
        for (var minute = 0; minute <= 600; minute += 10)
        {
            panels.Add(new PanelRecord
            {
                EndedAt = new DateTime(2026, 7, 6, 7, 0, 0).AddMinutes(minute),
                Outcome = PanelOutcome.Completed
            });
        }

        var day = ProductionAnalyser.Summarise(panels, Shift).Days.Single();

        Assert.Equal(0, day.UnplannedStopMinutes);
        Assert.Equal(1, day.Availability!.Value, 3);
    }

    [Fact]
    public void WithNoShiftModelAvailabilityIsNotReportedAtAll()
    {
        // Inventing a roster produces a number that looks measured and is not. Saying nothing is
        // the honest answer, and the output figures are still measured.
        var summary = ProductionAnalyser.Summarise(new[] { At(9, 0), At(15, 0) }, ShiftModel.NoShift);

        Assert.Null(summary.Availability);
        Assert.Null(summary.Days.Single().Availability);
        Assert.Equal(0, summary.PlannedMinutes);

        // Output is unaffected.
        Assert.Equal(2, summary.PanelsCompleted);
        Assert.Equal(1, summary.DaysWithOutput);
    }

    [Fact]
    public void TheRateIsQuotedBothWhileRunningAndAcrossTheShift()
    {
        // Quoting only one lets an availability problem read as a speed problem: a machine that
        // runs fast for four hours and sits idle for six is quick, and badly used.
        var panels = new List<PanelRecord>();
        for (var minute = 0; minute <= 240; minute += 10) panels.Add(At(9, 0) with
        {
            EndedAt = new DateTime(2026, 7, 6, 9, 0, 0).AddMinutes(minute)
        });

        var summary = ProductionAnalyser.Summarise(panels, Shift);

        Assert.NotNull(summary.RateWhileRunning);
        Assert.NotNull(summary.RateAcrossShift);
        Assert.True(summary.RateWhileRunning > summary.RateAcrossShift,
            $"{summary.RateWhileRunning} should beat {summary.RateAcrossShift}");
    }

    [Theory]
    [InlineData(7, 0, 8, 0, 60)]      // an hour of rostered time
    [InlineData(12, 0, 13, 30, 60)]   // 90 minutes of clock, 30 of it lunch
    [InlineData(6, 0, 8, 0, 60)]      // an hour of it before the shift started
    [InlineData(16, 0, 18, 0, 60)]    // an hour of it after the shift ended
    public void RosteredMinutesLeaveOutBreaksAndOffShiftTime(
        int fromHour, int fromMinute, int toHour, int toMinute, double expected)
    {
        var day = new DateTime(2026, 7, 6);

        Assert.Equal(expected, Shift.ProductiveMinutes(
            day.AddHours(fromHour).AddMinutes(fromMinute),
            day.AddHours(toHour).AddMinutes(toMinute)), 0);
    }
}
