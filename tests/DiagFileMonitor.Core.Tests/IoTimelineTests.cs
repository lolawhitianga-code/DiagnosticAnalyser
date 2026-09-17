using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class IoTimelineTests
{
    private static IoTimeline Build(params string[] lines) =>
        IoTimeline.Build(MachineLogFile.Parse(lines));

    private static SignalState Find(IEnumerable<SignalState> states, string name, string address = "") =>
        states.Single(s => s.Id.Name == name && (address.Length == 0 || s.Id.Address == address));

    /// <summary>
    /// The shape of a real Raked Extruder cycle: guns fire and release, then the clamps and
    /// supports come on and are still on when the step drops to zero.
    /// </summary>
    private static readonly string[] ExtruderCycle =
    {
        "07:53:30.1000000,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set On",
        "07:53:30.1000000,  OutputChange, LowerGunFire,  Output (COM7-6.2) Set On",
        "07:53:30.4089484,  OutputChange, LowerGunFire,  Output (COM7-6.5) Set Off",
        "07:53:30.4089484,  OutputChange, LowerGunFire,  Output (COM7-6.2) Set Off",
        "07:53:31.3300000,  InputChange, StudPinDown,  Input (COM7-2.3) Changed to 1",
        "07:53:31.9244951,  OutputChange, PlateSupport,  Output (COM7-5.2) Set On",
        "07:53:31.9244951,  OutputChange, PlateSupport,  Output (COM7-3.2) Set On",
        "07:53:33.9243917,  OutputChange, StudPinUp,  Output (COM7-4.5) Set On",
        "07:53:34.8774739,  OutputChange, TopStudClamp,  Output (COM7-6.7) Set On",
        "07:53:38.9085106,  Other, WallExtruderStep,  Step = 0",
        "07:53:38.9866323,  OutputChange, TopStudClamp,  Output (COM7-6.7) Set Off",
        "07:53:38.9866323,  OutputChange, StudPinUp,  Output (COM7-4.5) Set Off"
    };

    [Fact]
    public void ReportsWhatWasStillHeldOnAtAMoment()
    {
        // The question the whole feature exists to answer: reading the log line by line tells you
        // what changed, not what was already on.
        var snapshot = Build(ExtruderCycle).AtLine(10);

        Assert.Equal(4, snapshot.OutputsOn);
        Assert.Equal(1, snapshot.InputsOn);

        foreach (var name in new[] { "PlateSupport", "StudPinUp", "TopStudClamp" })
            Assert.True(snapshot.Outputs.Where(s => s.Id.Name == name).All(s => s.On), name);

        // The guns fired eight seconds earlier and are long since released.
        Assert.True(snapshot.Outputs.Where(s => s.Id.Name == "LowerGunFire").All(s => !s.On));
    }

    [Fact]
    public void AChangeCountsAtItsOwnLine()
    {
        var timeline = Build(ExtruderCycle);

        // Clicking the line that reads "TopStudClamp Set Off" should show it off, not on.
        Assert.False(Find(timeline.AtLine(11).Outputs, "TopStudClamp").On);
        Assert.True(Find(timeline.AtLine(10).Outputs, "TopStudClamp").On);
    }

    [Fact]
    public void OneNameOnTwoAddressesStaysTwoPoints()
    {
        // On M21737 LowerGunFire is two coils, one for each side of the machine. Keying on the
        // name alone would lose one of them.
        var timeline = Build(ExtruderCycle);

        var gun = timeline.Signals.Where(s => s.Name == "LowerGunFire").ToList();

        Assert.Equal(2, gun.Count);
        Assert.Equal(new[] { "COM7-6.2", "COM7-6.5" }, gun.Select(s => s.Address).OrderBy(a => a));
    }

    [Fact]
    public void OneAddressWithTwoNamesStaysTwoPoints()
    {
        // On the M20716 saw, 192.168.250.1-1.12 carries IO-OutfeedDriveTopClamp2Down and
        // ...3Down. They are logged separately and really do move apart, so keying on the
        // address alone would merge two outputs into one and report the wrong state.
        var timeline = Build(
            "09:48:41.6031504,  OutputChange, IO-OutfeedDriveTopClamp2Down,  Output (192.168.250.1-1.12) Set On",
            "09:48:41.9871510,  OutputChange, IO-OutfeedDriveTopClamp3Down,  Output (192.168.250.1-1.12) Set On",
            "09:48:49.5531649,  OutputChange, IO-OutfeedDriveTopClamp3Down,  Output (192.168.250.1-1.12) Set Off");

        var snapshot = timeline.AtLine(3);

        Assert.Equal(2, snapshot.Outputs.Count);
        Assert.True(Find(snapshot.Outputs, "IO-OutfeedDriveTopClamp2Down").On);
        Assert.False(Find(snapshot.Outputs, "IO-OutfeedDriveTopClamp3Down").On);

        var shared = Assert.Single(timeline.SharedAddresses);
        Assert.Equal("192.168.250.1-1.12", shared.Key);
    }

    [Fact]
    public void AnInputAndAnOutputCanShareAnAddressWithoutSharingAState()
    {
        // 192.168.250.1-0.2 is input FollowerUp and output IO-DeckRev on the same machine.
        var snapshot = Build(
            "10:53:27.3270000,  InputChange, FollowerUp,  Input (192.168.250.1-0.2) Changed to 1",
            "10:53:29.6990000,  OutputChange, IO-DeckRev,  Output (192.168.250.1-0.2) Set Off").AtLine(2);

        Assert.True(Assert.Single(snapshot.Inputs).On);
        Assert.False(Assert.Single(snapshot.Outputs).On);
    }

    [Fact]
    public void AStateNothingHasTouchedYetIsReadBackwardsAndSaysSo()
    {
        // The log opens mid-cycle. A clamp whose first event is "Set Off" was on before it, and
        // saying it was off because we had not seen it yet would be a lie.
        var snapshot = Build(
            "07:31:50.0000000,  Other, WallExtruderStep,  Step = 1000",
            "07:31:51.7561204,  OutputChange, TopStudClamp,  Output (COM7-6.7) Set Off",
            "07:31:54.4901558,  OutputChange, PlateSupport,  Output (COM7-5.2) Set On").AtLine(1);

        var clamp = Find(snapshot.Outputs, "TopStudClamp");
        Assert.True(clamp.On);
        Assert.Equal(StateSource.ReadBackFromNextChange, clamp.Source);
        Assert.Null(clamp.Since);

        // And one whose first event is "Set On" really was off.
        var support = Find(snapshot.Outputs, "PlateSupport");
        Assert.False(support.On);
        Assert.Equal(StateSource.ReadBackFromNextChange, support.Source);

        Assert.Equal(2, snapshot.ReadBackwards);
        Assert.Contains("read backwards", snapshot.Summary);
    }

    [Fact]
    public void AMeasuredStateSaysWhenItLastMovedAndHowOftenItMoves()
    {
        var state = Find(Build(ExtruderCycle).AtLine(10).Outputs, "PlateSupport", "COM7-5.2");

        Assert.Equal(StateSource.Measured, state.Source);
        Assert.Equal(new TimeSpan(0, 7, 53, 31, 924).TotalSeconds, state.Since!.Value.TotalSeconds, 2);
        Assert.Equal(6, state.SinceLine);
        Assert.Equal(1, state.ChangesTotal);
    }

    [Theory]
    [InlineData("Input (A) Changed to 1", true)]
    [InlineData("Input (A) Changed to 0", false)]
    [InlineData("Output (A) Set On", true)]
    [InlineData("Output (A) Set Off", false)]
    public void ReadsBothWordingsTheMachinesUse(string description, bool expected)
    {
        var kind = description.StartsWith("Input") ? "InputChange" : "OutputChange";
        var snapshot = Build($"08:00:00.0000000,  {kind}, Thing,  {description}").AtLine(1);

        Assert.Equal(expected, snapshot.Outputs.Concat(snapshot.Inputs).Single().On);
    }

    [Fact]
    public void AWordingThisBuildCannotReadIsCountedNotDropped()
    {
        // Silently skipping a line would make the reading wrong with nothing to show for it.
        var timeline = Build(
            "08:00:00.0000000,  OutputChange, Thing,  Output COM7-1.1 went a funny colour",
            "08:00:01.0000000,  OutputChange, Thing,  Output (COM7-1.1) Set On");

        Assert.Single(timeline.Unreadable);
        Assert.Single(timeline.Signals);
    }

    [Fact]
    public void ALogThatRunsPastMidnightIsCounted()
    {
        var timeline = Build(
            "23:59:59.0000000,  OutputChange, Thing,  Output (A) Set On",
            "00:00:01.0000000,  OutputChange, Thing,  Output (A) Set Off");

        Assert.Equal(1, timeline.Days);
        Assert.Equal(1, timeline.Changes[1].Day);
    }

    [Fact]
    public void ASmallBackwardsJumpInTheClockIsJitterNotANewDay()
    {
        // The sample saw log jumps back 1.3 seconds once in 100,000 lines. Calling that a new
        // day would put the whole rest of the file on the wrong side of midnight.
        var timeline = Build(
            "10:50:17.3618944,  OutputChange, Thing,  Output (A) Set On",
            "10:50:16.0731504,  OutputChange, Thing,  Output (A) Set Off");

        Assert.Equal(0, timeline.Days);
    }

    [Fact]
    public void ATimeResolvesToTheLastLineAtOrBeforeIt()
    {
        var timeline = Build(ExtruderCycle);

        Assert.Equal(10, timeline.LineAt(TimeSpan.Parse("07:53:38.9085106")));

        // A millisecond short of that line is still the line before it.
        Assert.Equal(9, timeline.LineAt(TimeSpan.Parse("07:53:38.9080000")));

        // Before anything in the log, the first line is the honest answer rather than nothing.
        Assert.Equal(1, timeline.LineAt(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void AnEmptyLogDoesNotThrow()
    {
        var snapshot = Build().AtLine(1);

        Assert.Empty(snapshot.Outputs);
        Assert.Empty(snapshot.Inputs);
        Assert.Equal(0, snapshot.LineNumber);
    }

    [Fact]
    public void EveryChangeToOnePointCanBeListed()
    {
        var timeline = Build(ExtruderCycle);
        var gun = timeline.Signals.Single(s => s.Name == "LowerGunFire" && s.Address == "COM7-6.5");

        var history = timeline.HistoryOf(gun);

        Assert.Equal(2, history.Count);
        Assert.True(history[0].On);
        Assert.False(history[1].On);
    }
}

public class MachineLogTimeTests
{
    [Theory]
    [InlineData("07:53", 7, 53, 0)]
    [InlineData("7:53", 7, 53, 0)]
    [InlineData("07:53:38", 7, 53, 38)]
    [InlineData("07:53:38.9085106", 7, 53, 38)]
    [InlineData("  07:53:38  ", 7, 53, 38)]
    public void ReadsTheWaysAPersonWritesATime(string text, int hour, int minute, int second)
    {
        Assert.True(MachineLogTime.TryParse(text, out var time));
        Assert.Equal(hour, time.Hours);
        Assert.Equal(minute, time.Minutes);
        Assert.Equal(second, time.Seconds);
    }

    [Fact]
    public void KeepsTheFractionRatherThanRoundingToTheSecond()
    {
        Assert.True(MachineLogTime.TryParse("07:53:38.9085106", out var time));
        Assert.Equal(TimeSpan.Parse("07:53:38.9085106"), time);
    }

    [Theory]
    [InlineData("981")]     // a line number typed in the time box - TryParse calls this 981 days
    [InlineData("8")]       // eight o'clock to a person, eight days to TryParse
    [InlineData("8.5")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("half past seven")]
    [InlineData("25:00:00")]
    [InlineData("1.07:53:38")]
    public void RefusesAnythingThatIsNotATimeOfDay(string text)
    {
        // Refusing is the point. Reading "8" as eight days lands past the end of every log, and
        // nothing on screen would tell the user their time was not understood.
        Assert.False(MachineLogTime.TryParse(text, out _));
    }

    [Fact]
    public void RefusesNull()
    {
        Assert.False(MachineLogTime.TryParse(null, out _));
    }
}
