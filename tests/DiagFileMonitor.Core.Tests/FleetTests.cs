using DiagFileMonitor.Core.Fleet;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// v1AI: the fleet layer. Lines here are copied from the real M22215 saw report and the real
/// StockList.xml that ships in every bundle.
/// </summary>
public class SawProductionTests
{
    private static readonly string[] RealLines =
    {
        "MachineStarted, 20260914 07:14:22",
        "MachineStarted, 20260914 07:14:22",
        "BoardStarted, 20260914 07:14:22, 4, 0, Custom Member,Custom Member,Custom Member,Custom Member",
        "MachineStopped, 20260914 07:15:15",
        "MachineStopped, 20260914 07:15:15",
        "MemberCut, 20260914 07:16:28, 1, 0, Custom Member, 0, 0.89",
        "BoardCompleted, 20260914 07:16:49, 4, 3584.3, 3.584, 5, Custom Member, 0, 890",
        "BoardCompleted, 20260914 07:16:49, 4, 3584.3, 3.584, 5, Custom Member, 0, 890",
        "BoardCompleted, 20260914 07:20:11, 3, 4645.2, 4.645, 4, Custom Member, 0, 1800",
        "UserLogin, 20260915 12:33:58, Spida"
    };

    [Fact]
    public void ReadsBoardsCutsAndOperatorsOutOfASawsOwnReport()
    {
        var saw = SawProduction.Read(RealLines, "M22215");

        Assert.Equal(2, saw.BoardsCompleted);
        Assert.Equal(1, saw.MembersCut);
        Assert.Equal(8.229, saw.LinealMetres, 3);
        Assert.Equal("Spida", Assert.Single(saw.Operators));
    }

    [Fact]
    public void TheDoubledEventsThisFormatWritesAreNotCountedTwice()
    {
        // Every MachineStarted and most BoardCompleted lines are written twice in the real file -
        // 706 duplicates in the M22215 sample. Counting them would double every figure.
        var saw = SawProduction.Read(RealLines, "M22215");

        Assert.Equal(3, saw.DuplicatesSkipped);
        Assert.Equal(2, saw.BoardsCompleted);
    }

    [Fact]
    public void MotorSpansAreNotCalledMachineHours()
    {
        // MachineStarted to MachineStopped has a median span of 25 seconds on the real file. It is
        // the saw motor, not the machine being switched on, and labelling it as run hours would
        // put a service interval on a number that is out by two orders of magnitude.
        var saw = SawProduction.Read(RealLines, "M22215");

        Assert.Equal(53, saw.MotorRunTime.TotalSeconds, 0);
    }

    [Fact]
    public void AStopThatNeverCameIsNotAWeekendOfRunning()
    {
        var saw = SawProduction.Read(new[]
        {
            "MachineStarted, 20260914 16:00:00",
            "MachineStopped, 20260917 08:00:00"
        }, "M1");

        Assert.Empty(saw.Runs);
        Assert.Equal(TimeSpan.Zero, saw.MotorRunTime);
    }

    [Fact]
    public void TheWorkingDayIsMeasuredFirstBoardToLastNotFromARoster()
    {
        var saw = SawProduction.Read(new[]
        {
            "BoardCompleted, 20260914 07:00:00, 1, 1000, 1.0, 1",
            "BoardCompleted, 20260914 09:00:00, 1, 1000, 1.0, 1",
            "BoardCompleted, 20260915 08:00:00, 1, 1000, 1.0, 1"
        }, "M1");

        var days = saw.WorkingDays;

        Assert.Equal(2, days.Count);
        Assert.Equal(TimeSpan.FromHours(2), days[0].Span);

        // One board on the second day gives a zero span, which must not become an infinite rate.
        Assert.Equal(TimeSpan.Zero, days[1].Span);
        Assert.Equal(3 / 2.0, saw.BoardsPerWorkingHour, 2);
    }

    [Fact]
    public void ABestDayNeedsEnoughBoardsToMeanSomething()
    {
        // Two boards ten minutes apart is 12 an hour and is not this machine's best day.
        var saw = SawProduction.Read(new[]
        {
            "BoardCompleted, 20260914 07:00:00, 1, 1000, 1.0, 1",
            "BoardCompleted, 20260914 07:10:00, 1, 1000, 1.0, 1"
        }, "M1");

        Assert.Equal(0, saw.BestDayRate);
    }

    [Fact]
    public void AMillimetreLengthIsUsedWhenTheMetreFieldIsMissing()
    {
        var saw = SawProduction.Read(new[] { "BoardCompleted, 20260914 07:00:00, 1, 3584.3" }, "M1");

        Assert.Equal(3.584, saw.LinealMetres, 3);
    }

    [Fact]
    public void AWordingThisBuildDoesNotKnowIsCountedNotDropped()
    {
        var saw = SawProduction.Read(new[]
        {
            "BoardCompleted, 20260914 07:00:00, 1, 1000, 1.0, 1",
            "SomethingNew, 20260914 07:01:00, 1"
        }, "M1");

        Assert.Equal(1, saw.Unreadable);
        Assert.Equal(1, saw.BoardsCompleted);
    }

    [Fact]
    public void AnExtrudersPanelReportProducesNoBoards()
    {
        // The two formats share a file name. A panel log must not be read as a saw log.
        var saw = SawProduction.Read(new[] { "PanelStarted, 20260706 07:10:00, 28" }, "AOR1694");

        Assert.Equal(0, saw.BoardsCompleted);
        Assert.False(saw.Any);
    }
}

public class TimberProfileTests
{
    /// <summary>Copied from a real StockList.xml, with the sizes the site had switched on.</summary>
    private const string RealStock = """
        <StockList>
          <StockItems>
            <StockItem>
              <StockLengths>
                <StockData><InUse>true</InUse><Length>5400</Length></StockData>
                <StockData><InUse>false</InUse><Length>4800</Length></StockData>
              </StockLengths>
              <InUse>true</InUse><Height>35</Height><Width>90</Width><Grade>*</Grade>
            </StockItem>
            <StockItem>
              <StockLengths>
                <StockData><InUse>true</InUse><Length>3600</Length></StockData>
              </StockLengths>
              <InUse>true</InUse><Height>45</Height><Width>290</Width><Grade>*</Grade>
            </StockItem>
            <StockItem>
              <StockLengths>
                <StockData><InUse>true</InUse><Length>6000</Length></StockData>
              </StockLengths>
              <InUse>false</InUse><Height>45</Height><Width>140</Width><Grade>*</Grade>
            </StockItem>
          </StockItems>
        </StockList>
        """;

    [Fact]
    public void ReadsOnlyTheSizesTheSiteHasSwitchedOn()
    {
        // The file carries every size the software knows about. Only the ones in use say
        // anything about what this customer builds.
        var timber = TimberProfileReader.Parse(RealStock);

        Assert.Equal(new[] { "35x90", "45x290" }, timber.Sizes);
        Assert.DoesNotContain("45x140", timber.Sizes);
    }

    [Fact]
    public void ReadsTheStockLengthsInUse()
    {
        var timber = TimberProfileReader.Parse(RealStock);

        Assert.Equal(new[] { 3600, 5400 }, timber.Lengths);
    }

    [Fact]
    public void TheDeepestMemberIsWhatDrivesMachineCapacity()
    {
        Assert.Equal(290, TimberProfileReader.Parse(RealStock).DeepestMember);
    }

    [Fact]
    public void AMissingFileIsUnknownRatherThanEmpty()
    {
        var timber = TimberProfileReader.Read("/no/such/StockList.xml");

        Assert.False(timber.Any);
        Assert.Equal(0, timber.DeepestMember);
    }
}

public class OpportunityRadarTests
{
    private static FleetSnapshot Machine(
        string serial = "M1", int bundles = 1, int recent = 0, int repeats = 0,
        string version = "V2.5.0.0", OutputRecord? output = null, TimberProfile? timber = null,
        int daysAgo = 1) => new()
    {
        SerialNumber = serial,
        Customer = "A Customer",
        Model = "SprintM600",
        SoftwareVersion = version,
        Bundles = bundles,
        RecentBundles = recent,
        RepeatSubmissions = repeats,
        FirstSeenUtc = DateTime.UtcNow.AddDays(-400),
        LastSeenUtc = DateTime.UtcNow.AddDays(-daysAgo),
        Output = output ?? OutputRecord.Nothing,
        Timber = timber ?? TimberProfile.Unknown
    };

    [Fact]
    public void AQuietFleetProducesAnEmptyList()
    {
        // A radar that lights up every week gets switched off.
        var radar = OpportunityRadar.Scan(new[] { Machine() }, DateTime.UtcNow);

        Assert.Empty(radar);
    }

    [Fact]
    public void AMachineWellUnderItsOwnBestIsWorthACall()
    {
        var output = new OutputRecord("boards", 500, 0, 1000, 10, 32, 46, null, null);

        var entry = Assert.Single(OpportunityRadar.Scan(new[] { Machine(output: output) }, DateTime.UtcNow));

        Assert.Contains(entry.Signals, s => s.Headline.Contains("under what it has already proved"));
        Assert.Contains("46", entry.Signals[0].Evidence);
        Assert.Contains("32", entry.Signals[0].Evidence);
    }

    [Fact]
    public void AMachineAtItsOwnBestIsLeftAlone()
    {
        var output = new OutputRecord("boards", 500, 0, 1000, 10, 44, 46, null, null);

        Assert.Empty(OpportunityRadar.Scan(new[] { Machine(output: output) }, DateTime.UtcNow));
    }

    [Fact]
    public void RepeatedBundlesRaiseASupportSignalNotASalesOne()
    {
        var entry = Assert.Single(
            OpportunityRadar.Scan(new[] { Machine(recent: 4, repeats: 2) }, DateTime.UtcNow));

        Assert.Equal("support", entry.Who);
        Assert.Contains("within hours", entry.Signals[0].Evidence);
    }

    [Fact]
    public void OldSoftwareIsFlaggedAndCurrentSoftwareIsNot()
    {
        Assert.Contains(OpportunityRadar.Scan(new[] { Machine(version: "V2.4.0.0") }, DateTime.UtcNow)
            .SelectMany(e => e.Signals), s => s.Headline.Contains("old software"));

        Assert.Empty(OpportunityRadar.Scan(new[] { Machine(version: "V2.5.0.0") }, DateTime.UtcNow));
    }

    [Fact]
    public void AMachineWeHaveOnlyEverSeenOnceCannotHaveGoneQuiet()
    {
        // One bundle a year ago is a machine we barely know, not a machine that stopped talking.
        Assert.Empty(OpportunityRadar.Scan(new[] { Machine(bundles: 1, daysAgo: 300) }, DateTime.UtcNow));
    }

    [Fact]
    public void RankingPutsTheLoudestMachineFirst()
    {
        var quiet = Machine("M1", version: "V2.4.0.0");
        var loud = Machine("M2", recent: 5, repeats: 3, version: "V2.4.0.0");

        var radar = OpportunityRadar.Scan(new[] { quiet, loud }, DateTime.UtcNow);

        Assert.Equal("M2", radar[0].Machine.SerialNumber);
        Assert.True(radar[0].Score > radar[1].Score);
    }
}
