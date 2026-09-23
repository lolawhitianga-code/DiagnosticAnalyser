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
    /// below - which is what the old offset guess leaned on, and why it was wrong.
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

    /// <summary>
    /// The finding, stated the way it can actually be stood behind: this model fits two plate
    /// support down sensors and only one moved, so the other never came on. Where the other one
    /// is numbered on THIS machine is not claimed - that is what went wrong the first time, when
    /// an offset read off three pairs said 0.3 and the real answer was 2.7 on one machine and
    /// something else again on the next.
    /// </summary>
    [Fact]
    public void KnowsAPartnerIsMissingWithoutClaimingItsNumber()
    {
        var signal = Assert.Single(
            WaitingOnCheck.Check(MachineLogFile.Parse(TheRealCase()), "RakingWallExtruderV3DG").Named);

        Assert.True(signal.PartnerMissing);
        Assert.Equal(2, signal.InstancesOnThisModel);
        Assert.Equal(1, signal.AddressesForThisName);
        Assert.True(signal.PartnerFromTheMap);
    }

    /// <summary>Both sides moved, so nothing is missing.</summary>
    [Fact]
    public void BothSidesMovingIsNotAMissingPartner()
    {
        var findings = WaitingOnCheck.Check(
            MachineLogFile.Parse(RealPairs
                .Append("07:49:30.000,  InputChange, PlateSupportDown,  Input (192.168.250.1-1.1) Changed to 1")
                .Append("07:49:31.000,  InputChange, PlateSupportDown,  Input (192.168.250.1-2.7) Changed to 1")
                .Append(TheMessage)
                .ToArray()),
            "RakingWallExtruderV3DG");

        Assert.Equal(2, findings.Named.Count);
        Assert.All(findings.Named, signal => Assert.False(signal.PartnerMissing));
    }

    /// <summary>
    /// With no list for the model there is nothing to say how many are fitted, so it does not
    /// guess that one is missing.
    /// </summary>
    [Fact]
    public void AModelWithNoListDoesNotGuessAtAMissingPartner()
    {
        Assert.False(Assert.Single(Check(TheRealCase()).Named).PartnerMissing);
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
    /// <summary>
    /// 83 addresses across three machines collapse to 48 named things, 35 of them fitted twice.
    /// That is the list worth handing a technician - what the machine has, not what one of them
    /// happened to number it.
    /// </summary>
    [Fact]
    public void KnowsTheRakedWallExtruderV3ByName()
    {
        var points = MachineIoMap.For("RakingWallExtruderV3DG");

        Assert.Equal(48, points.Count);
        Assert.Equal(35, points.Count(p => p.IsPaired));
        Assert.Equal(13, points.Count(p => !p.IsPaired));
        Assert.DoesNotContain(points.GroupBy(p => (p.Kind, MachineIoMap.Flatten(p.Name))), g => g.Count() > 1);
    }

    /// <summary>
    /// From the machine's Diagnostics screens. Not In Use points are left out, or the report
    /// would call every one a sensor that never came on.
    /// </summary>
    [Fact]
    public void KnowsTheComponentNailerV2ByName()
    {
        var points = MachineIoMap.For("ComponentNailerV2");

        Assert.Equal(33, points.Count);
        Assert.Equal(18, points.Count(p => p.Kind == SignalKind.Input));
        Assert.DoesNotContain(points, p => p.IsPaired);
        Assert.Null(MachineIoMap.Find("ComponentNailerV2", SignalKind.Input, "UpperGunWoodSensor"));
        Assert.Equal(new[] { "0.6" },
            MachineIoMap.Find("ComponentNailerV2", SignalKind.Input, "UpperGunLowerIsHigh")!.PointsSeen);
    }

    /// <summary>
    /// The numbering is not the identity. Support: "the actual number of the IO is less important
    /// than the name of the IO" - some machines run a point on node 5 and some on node 6.
    /// </summary>
    [Fact]
    public void TheMapIsNotKeyedOnTheNumbering()
    {
        var studPin = MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Output, "IO-StudPinUp2");

        Assert.NotNull(studPin);
        Assert.Equal(2, studPin!.Instances);
        Assert.Equal(new[] { "0.14", "1.9" }, studPin.PointsSeen);
    }

    /// <summary>One machine writes "E Stop" and another "Estop". Not two things.</summary>
    [Fact]
    public void NamesMatchAcrossSpacingAndCase()
    {
        Assert.True(MachineIoMap.SameName("E Stop", "Estop"));
        Assert.True(MachineIoMap.SameName("EStop", "e stop"));
        Assert.False(MachineIoMap.SameName("PlateSupportUp", "PlateSupportDown"));
    }

    /// <summary>
    /// Sides were measured, not guessed, and only fifteen points carry one. Unknown means
    /// exactly that.
    /// </summary>
    [Fact]
    public void OnlyCarriesASideWhereOneWasMeasured()
    {
        var plateClamp = MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Output, "IO-PlateClamp");
        var topStud = MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Output, "IO-TopStudClamp");

        Assert.Equal(MachineSide.FixedSide, plateClamp!.SideAtPoint("4.4"));
        Assert.Equal(MachineSide.FloatingSide, plateClamp.SideAtPoint("4.5"));
        Assert.Equal(MachineSide.Unknown, topStud!.SideAtPoint("4.14"));
    }

    /// <summary>
    /// Module 4 is not "lower bit is the fixed side". Three pairs run that way and the
    /// horizontal stud clamp runs the other, so a side read off a neighbour is a guess.
    /// </summary>
    [Fact]
    public void TheSidesDoNotFollowABitOrder()
    {
        var clamp = MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Output, "IO-PlateClamp");
        var horiz = MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Output, "IO-HorizStudClamp");

        Assert.Equal(MachineSide.FixedSide, clamp!.SideAtPoint("4.4"));
        Assert.Equal(MachineSide.FloatingSide, horiz!.SideAtPoint("4.12"));
        Assert.Equal(MachineSide.FixedSide, horiz.SideAtPoint("4.13"));
    }

    [Fact]
    public void NamesAPointTheWayATechnicianWouldSayIt()
    {
        var clamp = MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Output, "IO-PlateClamp");

        Assert.Equal("IO-PlateClamp (fixed side) at 4.4", clamp!.Describe("4.4"));
        Assert.Equal("IO-PlateClamp", clamp.Describe());
    }

    [Fact]
    public void AModelWeHaveNeverMappedGetsNothingRatherThanAGuess()
    {
        Assert.Empty(MachineIoMap.For("SprintM600"));
        Assert.Empty(MachineIoMap.For(null));
        Assert.Null(MachineIoMap.Find("SprintM600", SignalKind.Input, "PlateSupportDown"));
    }

    /// <summary>
    /// The map knows a plate support down sensor is fitted twice. It does not claim to know what
    /// this machine numbers the second one - that was the M21737 mistake.
    /// </summary>
    [Fact]
    public void KnowsAPointIsFittedTwiceWithoutClaimingItsNumberHere()
    {
        var found = MachineIoMap.Find("RakingWallExtruderV3DG", SignalKind.Input, "PlateSupportDown");

        Assert.NotNull(found);
        Assert.Equal(2, found!.Instances);
        Assert.True(found.IsPaired);
    }

    /// <summary>
    /// M21868: "Axis Disabled" is a motion event, but "Axis Enable" comes under Other. Missing it
    /// reported StudTrolley disabled since 10:43:25 while it was moving at 10:52.
    /// </summary>
    [Fact]
    public void An_axis_switched_back_on_is_not_reported_disabled()
    {
        var findings = WaitingOnCheck.Check(MachineLogFile.Parse(new[]
        {
            "10:43:25.091,  MotionEvent, StudTrolley,  Axis Disabled",
            "10:43:26.624,  Other, StudTrolley,  Axis Enable",
            "10:52:57.480,  Other, ComponentNailerV2PLC,  Step Condition, Waiting for Trolley Back-off"
        }));

        var axis = Assert.Single(findings.Axes);
        Assert.Equal("StudTrolley", axis.Name);
        Assert.True(axis.Enabled);
        Assert.True(axis.Ready);
        Assert.Empty(findings.NotReady);
    }

    [Fact]
    public void An_axis_still_disabled_is_still_reported()
    {
        var findings = WaitingOnCheck.Check(MachineLogFile.Parse(new[]
        {
            "10:43:25.091,  MotionEvent, StudTrolley,  Axis Disabled",
            "10:52:57.480,  Other, ComponentNailerV2PLC,  Step Condition, Waiting for Trolley Back-off"
        }));

        Assert.Equal("Disabled", Assert.Single(findings.NotReady).State);
    }
}
