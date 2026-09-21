using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// Built from the real M21737 export from PlaceMakers Wiri, where the log ended repeating
/// "Waiting for Both Panel Height Servos in position and PlateSupports Down".
/// </summary>
public class WaitingOnTests
{
    private static WaitingOnFindings Check(params string[] lines) =>
        WaitingOnCheck.Check(MachineLogFile.Parse(lines));

    private const string TheMessage =
        "07:58:26.902,  Other, Extruder,  Waiting for Both Panel Height Servos in position and PlateSupports Down'";

    /// <summary>
    /// The pairs this machine really logs. Every one is the same bit two lower on the module
    /// below, which is what lets the missing partner's address be worked out.
    /// </summary>
    private static readonly string[] RealPairs =
    {
        "07:47:15.190,  InputChange, TrolleyBottomClampOpen,  Input (192.168.250.1-1.4) Changed to 1",
        "07:47:15.190,  InputChange, TrolleyBottomClampOpen,  Input (192.168.250.1-0.6) Changed to 1",
        "07:47:19.689,  InputChange, TrolleyTopClampOpen,  Input (192.168.250.1-1.6) Changed to 1",
        "07:47:22.190,  InputChange, TrolleyTopClampOpen,  Input (192.168.250.1-0.8) Changed to 1",
        "07:48:07.903,  InputChange, PlateClampUp,  Input (192.168.250.1-1.8) Changed to 1",
        "07:48:07.903,  InputChange, PlateClampUp,  Input (192.168.250.1-0.10) Changed to 1"
    };

    private static string[] TheRealCase() => RealPairs
        .Append("07:49:29.628,  InputChange, PlateSupportDown,  Input (192.168.250.1-1.1) Changed to 1")
        .Append("08:16:14.962,  MotionEvent, FloatingSideHeight,  Axis Disabled")
        .Append("08:16:14.929,  MotionEvent, TrolleyHeight,  Axis Disabled")
        .Append(TheMessage)
        .ToArray();

    [Fact]
    public void ReadsWhatTheMachineSaidItWasWaitingFor()
    {
        var findings = Check(TheRealCase());

        Assert.True(findings.Any);
        Assert.Contains("PlateSupports Down", findings.Message);
        Assert.Equal("Extruder", findings.Tag);
        Assert.True(findings.StillWaitingAtTheEnd);
    }

    [Fact]
    public void MatchesAnInputNamedInThePluralToItsSingularSignal()
    {
        // The machine writes "PlateSupports Down" for an input called PlateSupportDown.
        var signal = Assert.Single(Check(TheRealCase()).Named);

        Assert.Equal("PlateSupportDown", signal.Id.Name);
        Assert.True(signal.On);
    }

    [Fact]
    public void WorksOutWhereTheMissingPartnerWouldBe()
    {
        // The whole finding. Every pair on this machine is the same bit two lower on the module
        // below - 1.8 with 0.10, 1.6 with 0.8, 1.4 with 0.6 - so the partner of 1.1 is 0.3, and
        // 0.3 never appears anywhere in the log. An input that never came on leaves no trace.
        var signal = Assert.Single(Check(TheRealCase()).Named);

        Assert.True(signal.PartnerMissing);
        Assert.Equal("192.168.250.1-0.3", signal.MissingPartnerAddress);
    }

    [Fact]
    public void AnAddressAlreadyInUseIsNotAMissingPartner()
    {
        var findings = Check(TheRealCase()
            .Append("07:50:00.000,  InputChange, SomethingElse,  Input (192.168.250.1-0.3) Changed to 1")
            .ToArray());

        Assert.False(Assert.Single(findings.Named).PartnerMissing);
    }

    [Fact]
    public void NoPairingHabitMeansNoGuessAtAPartner()
    {
        // With nothing to learn the convention from, saying where a partner would be is invention.
        var findings = Check(
            "07:49:29.628,  InputChange, PlateSupportDown,  Input (192.168.250.1-1.1) Changed to 1",
            TheMessage);

        Assert.False(Assert.Single(findings.Named).PartnerMissing);
    }

    [Fact]
    public void ReadsTheAxesTheMessageNamesAndWhetherTheyAreInPosition()
    {
        var axes = Check(TheRealCase()).Axes;

        Assert.Equal(2, axes.Count);
        Assert.All(axes, a => Assert.False(a.Ready));
        Assert.Contains(axes, a => a.Name == "FloatingSideHeight");
        Assert.Contains(axes, a => a.Name == "TrolleyHeight");
    }

    [Fact]
    public void AnAxisSharingOnlyAGenericWordIsNotNamed()
    {
        // "Panel Height Servos" must not drag in FloatingEjectServo just because both say servo.
        var findings = Check(TheRealCase()
            .Append("08:16:14.900,  MotionEvent, FloatingEjectServo,  Axis Disabled")
            .ToArray());

        Assert.DoesNotContain(findings.Axes, a => a.Name == "FloatingEjectServo");
    }

    [Fact]
    public void AClampInputIsNotDraggedIntoAMessageAboutPlateSupports()
    {
        // Every word of the signal's name has to appear, or PlateClampUp answers a question about
        // plate supports.
        Assert.DoesNotContain(Check(TheRealCase()).Named, s => s.Id.Name == "PlateClampUp");
    }

    [Fact]
    public void ReadsAWaitingMessageThatIsPartOfALongerLine()
    {
        // The common shape across these machines, and anchoring the pattern at the start of the
        // line missed 4,123 of them in one sample log.
        var findings = Check(
            "13:08:23.000,  Other, ControlInfeed,  Step Condition, Waiting for Follower Arm UP",
            "13:08:24.000,  InputChange, FollowerUp,  Input (192.168.250.1-0.2) Changed to 0");

        Assert.Contains("Follower Arm UP", findings.Message);
    }

    [Fact]
    public void SaysSoWhenTheThingItNamedIsOff()
    {
        var findings = Check(
            "13:08:20.000,  InputChange, FollowerUp,  Input (192.168.250.1-0.2) Changed to 0",
            "13:08:23.000,  Other, ControlInfeed,  Step Condition, Waiting for Follower Arm UP");

        var signal = Assert.Single(findings.Named);

        Assert.False(signal.On);
        Assert.True(findings.SomethingIsUnsatisfied);
    }

    [Fact]
    public void AMessageNamingNothingWeKnowStaysQuiet()
    {
        // The M22215 saw repeats "waiting for clamps" 369 times and has no signal by that name.
        // Guessing at one would be worse than saying nothing.
        var findings = Check(
            "06:54:02.000,  Other, CutBoardPLC,  waiting for clamps",
            "06:54:03.000,  InputChange, StudPinDown,  Input (COM7-2.3) Changed to 1");

        Assert.Empty(findings.Named);
        Assert.False(findings.Any);
    }

    [Fact]
    public void ALogWithNoWaitingMessageSaysNothing()
    {
        Assert.False(Check("07:53:38.908,  Other, WallExtruderStep,  Step = 0").Any);
        Assert.False(Check().Any);
    }

    [Theory]
    [InlineData("Both Panel Height Servos in position and PlateSupports Down", "PlateSupportDown", true)]
    [InlineData("Both Panel Height Servos in position and PlateSupports Down", "PlateClampUp", false)]
    [InlineData("Follower Arm UP", "FollowerUp", true)]
    [InlineData("OutfeedDriveSensor1 and OutfeedDriveSensor3 Off", "OutfeedDriveSensor1", true)]
    [InlineData("OutfeedDriveSensor1 and OutfeedDriveSensor3 Off", "OutfeedDriveSensor2", false)]
    [InlineData("Infeed Drive Top Clamps up", "IO-InfeedDriveTopClamp1Up", false)]
    public void MatchesASignalNameToTheWordsTheMachineUses(string message, string signal, bool expected)
    {
        Assert.Equal(expected, WaitingOnCheck.Mentions(message, signal));
    }
}

/// <summary>
/// The I/O map, read off an 82,149 line M21737 log covering nine and a half hours of production.
/// </summary>
public class MachineIoMapTests
{
    [Fact]
    public void KnowsTheRakedWallExtruderV3()
    {
        var points = MachineIoMap.For("RakingWallExtruderV3DG");

        // 75 from the M21737 log, plus the 8 the M21844 log exercised and it did not.
        Assert.Equal(83, points.Count);
        Assert.Equal(43, points.Count(p => p.Kind == SignalKind.Input));
        Assert.Equal(40, points.Count(p => p.Kind == SignalKind.Output));
        Assert.DoesNotContain(points.GroupBy(p => (p.Kind, p.Address)), g => g.Count() > 1);
    }

    /// <summary>
    /// The sides that are set were measured. The rest are Unknown and must stay that way -
    /// an Unknown side is the map saying it does not know, not an oversight to be filled in.
    /// </summary>
    [Fact]
    public void OnlyCarriesASideWhereOneWasMeasured()
    {
        var points = MachineIoMap.For("RakingWallExtruderV3DG");

        Assert.Equal(15, points.Count(p => p.Side != MachineSide.Unknown));

        // Settled 25 times out of 25 by the machine's own "Fixed Product: False" messages.
        Assert.Equal(MachineSide.FixedSide,
            MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Input, "PlatePresentSwitch")
                .Single(p => p.Address == "192.168.250.1-4.2").Side);
        Assert.Equal(MachineSide.FloatingSide,
            MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Input, "PlatePresentSwitch")
                .Single(p => p.Address == "192.168.250.1-4.4").Side);
    }

    /// <summary>
    /// Module 4 is not "lower bit is the fixed side". The gripper, plate clamp and upper gripper
    /// pairs run that way and the horizontal stud clamp runs the other way, so a side read off a
    /// neighbouring pair is a guess. Both of these came from maint_data.json, separately.
    /// </summary>
    [Fact]
    public void TheSidesDoNotFollowABitOrder()
    {
        MachineSide Side(string address) =>
            MachineIoMap.For("RakingWallExtruderV3DG")
                .Single(p => p.Kind == SignalKind.Output && p.Address == address).Side;

        Assert.Equal(MachineSide.FixedSide, Side("192.168.250.1-4.4"));    // PlateClamp, low bit
        Assert.Equal(MachineSide.FloatingSide, Side("192.168.250.1-4.5"));
        Assert.Equal(MachineSide.FloatingSide, Side("192.168.250.1-4.12")); // HorizStudClamp, reversed
        Assert.Equal(MachineSide.FixedSide, Side("192.168.250.1-4.13"));
    }

    [Fact]
    public void NamesAPointTheWayATechnicianWouldSayIt()
    {
        var fixedClamp = MachineIoMap.For("RakingWallExtruderV3DG")
            .Single(p => p.Kind == SignalKind.Output && p.Address == "192.168.250.1-4.4");
        var unsided = MachineIoMap.For("RakingWallExtruderV3DG")
            .Single(p => p.Kind == SignalKind.Output && p.Address == "192.168.250.1-4.14");

        Assert.Equal("IO-PlateClamp (fixed side, 192.168.250.1-4.4)", fixedClamp.Describe());
        Assert.Equal("IO-TopStudClamp (192.168.250.1-4.14)", unsided.Describe());
    }

    [Fact]
    public void KnowsBothPlateSupportDownInputs()
    {
        // The whole point. The short M21737 log only ever showed 1.1, and the second one at 2.7
        // never changed - which is what a plate support that never came down looks like.
        var found = MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Input, "PlateSupportDown");

        Assert.Equal(2, found.Count);
        Assert.Equal(new[] { "192.168.250.1-1.1", "192.168.250.1-2.7" },
            found.Select(p => p.Address).OrderBy(a => a));
        Assert.All(found, p => Assert.True(p.IsPaired));
    }

    [Fact]
    public void ThePairingOffsetIsNotOneNumber()
    {
        // Why guessing an address from the log's own pairs was wrong: modules 0 and 1 pair two
        // bits apart, module 1 pairs eleven apart internally, module 4 pairs adjacent.
        string Partner(SignalKind kind, string name, string address) =>
            MachineIoMap.Find("RakingWallExtruderV3DG", kind, name)
                .Single(p => p.Address == address).PartnerAddress;

        Assert.Equal("192.168.250.1-1.8", Partner(SignalKind.Input, "PlateClampUp", "192.168.250.1-0.10"));
        Assert.Equal("192.168.250.1-2.7", Partner(SignalKind.Input, "PlateSupportDown", "192.168.250.1-1.1"));
        Assert.Equal("192.168.250.1-4.15", Partner(SignalKind.Output, "IO-TopStudClamp", "192.168.250.1-4.14"));
    }

    [Fact]
    public void AModelWeHaveNeverMappedGetsNothingRatherThanAGuess()
    {
        Assert.Empty(MachineIoMap.For("SprintM600"));
        Assert.Empty(MachineIoMap.For(null));
        Assert.Empty(MachineIoMap.Find("SprintM600", SignalKind.Input, "PlateSupportDown"));
    }

    [Fact]
    public void TheMapAnswersTheMissingPartnerRatherThanTheOffsetGuess()
    {
        // Same short log as the case, now read against the model's real map. Before this the
        // derived offset said 192.168.250.1-0.3, which is not used on this machine at all.
        var findings = WaitingOnCheck.Check(MachineLogFile.Parse(new[]
        {
            "07:47:15.190,  InputChange, TrolleyBottomClampOpen,  Input (192.168.250.1-1.4) Changed to 1",
            "07:47:15.190,  InputChange, TrolleyBottomClampOpen,  Input (192.168.250.1-0.6) Changed to 1",
            "07:48:07.903,  InputChange, PlateClampUp,  Input (192.168.250.1-1.8) Changed to 1",
            "07:48:07.903,  InputChange, PlateClampUp,  Input (192.168.250.1-0.10) Changed to 1",
            "07:49:29.628,  InputChange, PlateSupportDown,  Input (192.168.250.1-1.1) Changed to 1",
            "07:58:26.902,  Other, Extruder,  Waiting for Both Panel Height Servos in position and PlateSupports Down'"
        }), "RakingWallExtruderV3DG");

        var signal = Assert.Single(findings.Named);

        Assert.True(signal.PartnerMissing);
        Assert.True(signal.PartnerFromTheMap);
        Assert.Equal("192.168.250.1-2.7", signal.MissingPartnerAddress);
    }
}
