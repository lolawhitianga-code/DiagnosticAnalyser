using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class IoMapComparisonTests
{
    private static IoMapFindings Check(params string[] lines) =>
        IoMapComparison.Check(
            IoTimeline.Build(MachineLogFile.Parse(lines)),
            "RakingWallExtruderV3DG",
            ControlPlatform.NetworkNodes);

    /// <summary>
    /// The real M20771 finding. Three machines of this model agree on 74 addresses, and then
    /// this one has UpperGunUpperIsLow a bit lower than the other two. Quoting the map as fact
    /// would have sent somebody to the wrong terminal.
    /// </summary>
    [Fact]
    public void CatchesANameSittingAtAnAddressTheMapDoesNotHave()
    {
        var findings = Check(
            "07:35:10.0000000,  InputChange, UpperGunUpperIsLow,  Input (192.168.250.1-0.18) Changed to 1",
            "07:35:11.0000000,  InputChange, UpperGunUpperIsLow,  Input (192.168.250.1-2.1) Changed to 1");

        var clash = Assert.Single(findings.Disagreements);

        Assert.Equal("UpperGunUpperIsLow", clash.Name);
        Assert.Equal(new[] { "192.168.250.1-0.18" }, clash.InThisLog);
        Assert.Contains("192.168.250.1-0.19", clash.InTheMap);
        Assert.True(findings.AnythingWorrying);
    }

    /// <summary>
    /// A short log showing one half of a pair is ordinary. Raising it would bury the one finding
    /// that matters under a page of things that are fine.
    /// </summary>
    [Fact]
    public void OneHalfOfAPairIsNotADisagreement()
    {
        var findings = Check(
            "07:35:10.0000000,  InputChange, PlateSupportDown,  Input (192.168.250.1-1.1) Changed to 1");

        Assert.Empty(findings.Disagreements);
        Assert.False(findings.AnythingWorrying);
        Assert.Equal(1, findings.Confirmed);
    }

    /// <summary>
    /// M20771 drives output 5.0 as IO-Bay2Stops while the infeed runs and IO-UnloaderUp while
    /// the unloader does. One physical point, a label per job - so "what is 5.0" has two right
    /// answers and the report has to give both.
    /// </summary>
    [Fact]
    public void ReportsAnAddressTheMachineCallsByTwoNames()
    {
        var findings = Check(
            "07:35:32.0000000,  OutputChange, IO-Bay2Stops,  Output (192.168.250.1-5.0) Set On",
            "07:36:14.0000000,  OutputChange, IO-Bay2Stops,  Output (192.168.250.1-5.0) Set Off",
            "07:41:33.0000000,  OutputChange, IO-UnloaderUp,  Output (192.168.250.1-5.0) Set On",
            "07:43:37.0000000,  OutputChange, IO-UnloaderUp,  Output (192.168.250.1-5.0) Set Off");

        var shared = Assert.Single(findings.SharedAddresses);

        Assert.Equal("192.168.250.1-5.0", shared.Address);
        Assert.Equal(new[] { "IO-Bay2Stops", "IO-UnloaderUp" }, shared.Names);
    }

    /// <summary>
    /// "E Stop" and "Estop" are one point spelled two ways, so they must not read as the name
    /// being at an address the map does not have.
    /// </summary>
    [Fact]
    public void SpellingDifferencesAreNotTreatedAsADifferentSignal()
    {
        var findings = Check(
            "07:35:10.0000000,  InputChange, E Stop,  Input (192.168.250.1-4.0) Changed to 0",
            "07:35:11.0000000,  InputChange, Estop,  Input (192.168.250.1-4.0) Changed to 1");

        Assert.Empty(findings.Disagreements);
        Assert.Single(findings.SharedAddresses);
    }

    /// <summary>
    /// M20771 carries an infeed, lifter and unloader on modules 3 and 5 that neither mapped
    /// machine has. That is an option, not a fault, and it reads differently from a
    /// disagreement.
    /// </summary>
    [Fact]
    public void PointsTheMapHasNeverSeenAreListedSeparatelyFromDisagreements()
    {
        var findings = Check(
            "07:35:32.0000000,  OutputChange, IO-Bay1Stops,  Output (192.168.250.1-5.8) Set On",
            "07:35:33.0000000,  InputChange, Bay1Prox,  Input (192.168.250.1-3.13) Changed to 1");

        Assert.Empty(findings.Disagreements);
        Assert.Equal(2, findings.NotInTheMap.Count);
    }

    /// <summary>With no map for the model there is nothing to check against, and it says so.</summary>
    [Fact]
    public void AModelWithNoMapIsNotChecked()
    {
        var findings = IoMapComparison.Check(
            IoTimeline.Build(MachineLogFile.Parse(new[]
            {
                "07:35:10.0000000,  InputChange, Something,  Input (192.168.250.1-0.1) Changed to 1"
            })),
            "SprintM600",
            ControlPlatform.NetworkNodes);

        Assert.False(findings.Checked);
        Assert.Empty(findings.Disagreements);
    }
}
