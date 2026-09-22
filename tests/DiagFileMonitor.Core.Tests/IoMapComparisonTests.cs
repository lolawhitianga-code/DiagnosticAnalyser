using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class IoMapComparisonTests
{
    private static IoMapFindings Check(params string[] lines) =>
        IoMapComparison.Check(IoTimeline.Build(MachineLogFile.Parse(lines)), "RakingWallExtruderV3DG");

    private static PointHere Row(IoMapFindings findings, string name) =>
        findings.Known.Single(r => MachineIoMap.SameName(r.Name, name));

    /// <summary>
    /// The list support asked for: the name, and whatever number this machine happens to use.
    /// The numbering is stripped of its transport prefix because that is not what anyone reads
    /// off the cabinet.
    /// </summary>
    [Fact]
    public void ListsTheNameWithThisMachinesOwnNumbers()
    {
        var findings = Check(
            "07:35:10.0000000,  OutputChange, IO-PlateClamp,  Output (192.168.250.1-4.4) Set On",
            "07:35:10.1000000,  OutputChange, IO-PlateClamp,  Output (192.168.250.1-4.5) Set On");

        var row = Row(findings, "IO-PlateClamp");

        Assert.Equal(new[] { "4.4", "4.5" }, row.PointsHere);
        Assert.Equal("4.4 fixed, 4.5 floating", row.Numbers);
        Assert.False(row.SomeNeverMoved);
    }

    /// <summary>
    /// The M20771 case, and the whole point of the rework. UpperGunUpperIsLow sits at 0.18 there
    /// and 0.19 on the other two machines of the model. That is not a fault and must not read
    /// like one - it is just how that machine is numbered.
    /// </summary>
    [Fact]
    public void ADifferentNumberOnThisMachineIsNotAProblem()
    {
        var findings = Check(
            "07:35:10.0000000,  InputChange, UpperGunUpperIsLow,  Input (192.168.250.1-0.18) Changed to 1",
            "07:35:11.0000000,  InputChange, UpperGunUpperIsLow,  Input (192.168.250.1-2.1) Changed to 1");

        var row = Row(findings, "UpperGunUpperIsLow");

        Assert.Equal(new[] { "0.18", "2.1" }, row.PointsHere);
        Assert.False(row.SomeNeverMoved);
        Assert.Empty(findings.NotOnTheModel);
    }

    /// <summary>
    /// The finding that still matters. The model fits two of these and only one moved, so the
    /// other either never came on or is not there - and a change log cannot tell those apart.
    /// What it does not do is name a number for it; that would be another machine's.
    /// </summary>
    [Fact]
    public void SaysWhenTheModelFitsMoreThanMovedHere()
    {
        var findings = Check(
            "07:35:10.0000000,  InputChange, PlateSupportDown,  Input (192.168.250.1-1.1) Changed to 1");

        var row = Row(findings, "PlateSupportDown");

        Assert.True(row.SomeNeverMoved);
        Assert.Equal(2, row.InstancesOnModel);
        Assert.Single(row.PointsHere);
        Assert.Contains(findings.NeverMoved, r => MachineIoMap.SameName(r.Name, "PlateSupportDown"));
    }

    /// <summary>A name on the model that never appeared at all is the same kind of finding.</summary>
    [Fact]
    public void ListsNamesTheModelHasThatNeverMovedHere()
    {
        var findings = Check(
            "07:35:10.0000000,  OutputChange, IO-RackLock,  Output (192.168.250.1-0.1) Set On");

        Assert.Contains(findings.NeverMoved, r => MachineIoMap.SameName(r.Name, "IO-SideClamp"));
        Assert.DoesNotContain(findings.NeverMoved, r => MachineIoMap.SameName(r.Name, "IO-RackLock"));
    }

    /// <summary>
    /// M20771 drives output 5.0 as IO-Bay2Stops while the infeed runs and IO-UnloaderUp while
    /// the unloader does. One physical point, a label per job.
    /// </summary>
    [Fact]
    public void ReportsANumberTheMachineCallsByTwoNames()
    {
        var findings = Check(
            "07:35:32.0000000,  OutputChange, IO-Bay2Stops,  Output (192.168.250.1-5.0) Set On",
            "07:41:33.0000000,  OutputChange, IO-UnloaderUp,  Output (192.168.250.1-5.0) Set On");

        var shared = Assert.Single(findings.SharedAddresses);
        Assert.Equal(new[] { "IO-Bay2Stops", "IO-UnloaderUp" }, shared.Names);
    }

    /// <summary>"E Stop" and "Estop" are one input spelled two ways, not two inputs.</summary>
    [Fact]
    public void SpellingDifferencesAreOnePoint()
    {
        var findings = Check(
            "07:35:10.0000000,  InputChange, E Stop,  Input (192.168.250.1-4.0) Changed to 0",
            "07:35:11.0000000,  InputChange, Estop,  Input (192.168.250.1-4.0) Changed to 1");

        var row = Row(findings, "EStop");

        Assert.Equal(new[] { "4.0" }, row.PointsHere);
        Assert.Empty(findings.NotOnTheModel);
    }

    /// <summary>
    /// M20771's infeed, lifter and unloader are not on the model's list because neither mapped
    /// machine has them. An option, not a fault.
    /// </summary>
    [Fact]
    public void PointsTheModelHasNeverSeenAreListedAsOptions()
    {
        var findings = Check(
            "07:35:32.0000000,  OutputChange, IO-Bay1Stops,  Output (192.168.250.1-5.8) Set On",
            "07:35:33.0000000,  InputChange, Bay1Prox,  Input (192.168.250.1-3.13) Changed to 1");

        Assert.Equal(2, findings.NotOnTheModel.Count);
    }

    [Fact]
    public void AModelWithNoListIsNotChecked()
    {
        var findings = IoMapComparison.Check(
            IoTimeline.Build(MachineLogFile.Parse(new[]
            {
                "07:35:10.0000000,  InputChange, Something,  Input (192.168.250.1-0.1) Changed to 1"
            })),
            "SprintM600");

        Assert.False(findings.Checked);
        Assert.Empty(findings.Known);
    }

    /// <summary>A CLX number reads the same way once the transport prefix is off.</summary>
    [Theory]
    [InlineData("192.168.250.1-4.2", "4.2")]
    [InlineData("TCP192.168.50.2-3.17", "3.17")]
    [InlineData("COM7-6.5", "6.5")]
    public void StripsTheTransportPrefixOffTheNumber(string address, string expected) =>
        Assert.Equal(expected, ControlPlatformCheck.Point(address));
}
