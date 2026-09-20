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
