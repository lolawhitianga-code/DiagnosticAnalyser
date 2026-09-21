using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class MaintenanceCountersTests
{
    /// <summary>Trimmed from a real M21844 CloudLog/maint_data.json.</summary>
    private const string RealShape = """
        {"TwoStates":{
          "FixedSide/PlateClamp":{"_onCount":47,"_offCount":47,"_onTime":128.2613231},
          "FloatingSide/PlateClamp":{"_onCount":46,"_offCount":46,"_onTime":131.1016769},
          "CommonIO/RackLock":{"_onCount":63,"_offCount":63,"_onTime":266.51330409999997},
          "ControlBox2/FireLamp":{"_onCount":73,"_offCount":74,"_onTime":1122.0773708}
        },"Servos":{},"Date":"2026-09-08T01:00:00Z"}
        """;

    [Fact]
    public void ReadsTheSideOffTheName()
    {
        var duties = MaintenanceCounters.Parse(RealShape);

        Assert.Equal(4, duties.Count);
        Assert.Equal(MachineSide.FixedSide, duties.Single(d => d.FullName == "FixedSide/PlateClamp").Side);
        Assert.Equal(MachineSide.FloatingSide, duties.Single(d => d.FullName == "FloatingSide/PlateClamp").Side);
        Assert.Equal(MachineSide.Shared, duties.Single(d => d.FullName == "CommonIO/RackLock").Side);
    }

    /// <summary>
    /// The two control boxes are the operator stations either end of the machine, not a
    /// fixed/floating split. Reading ControlBox2 as "the floating side" would be an invention.
    /// </summary>
    [Fact]
    public void ControlBoxesAreSharedNotSided()
    {
        var duties = MaintenanceCounters.Parse(RealShape);

        Assert.Equal(MachineSide.Shared, duties.Single(d => d.FullName == "ControlBox2/FireLamp").Side);
    }

    [Fact]
    public void ShortNameIsWhatTheMachineLogCallsIt()
    {
        var duties = MaintenanceCounters.Parse(
            """{"TwoStates":{"FloatingSide/DualGuns/UpperGunFire":{"_onCount":60,"_offCount":60,"_onTime":20.5}}}""");

        Assert.Equal("UpperGunFire", duties.Single().ShortName);
    }

    [Fact]
    public void AMissingFileIsNotAnError()
    {
        Assert.Empty(MaintenanceCounters.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.Null(MaintenanceCounters.ReadStamp(null));
    }

    [Fact]
    public void RubbishInTheFileIsNotAnError()
    {
        Assert.Empty(MaintenanceCounters.Parse("""{"TwoStates":"not an object"}"""));
        Assert.Empty(MaintenanceCounters.Parse("{}"));
    }
}

public class IoSideResolverTests
{
    /// <summary>
    /// Two sides that ran for different lengths of time. The floating clamp is held about three
    /// seconds longer, so the on-times separate and each address can be named.
    /// </summary>
    private static readonly string[] SidesThatDiffer =
    {
        "12:00:10.0000000,  OutputChange, IO-PlateClamp,  Output (1-4.4) Set On",
        "12:00:10.0000000,  OutputChange, IO-PlateClamp,  Output (1-4.5) Set On",
        "12:00:20.0000000,  OutputChange, IO-PlateClamp,  Output (1-4.4) Set Off",
        "12:00:23.0000000,  OutputChange, IO-PlateClamp,  Output (1-4.5) Set Off",
        "12:00:30.0000000,  Other, WallExtruderStep,  Step = 0"
    };

    private const string CountersThatDiffer = """
        {"TwoStates":{
          "FixedSide/PlateClamp":{"_onCount":1,"_offCount":1,"_onTime":10.0},
          "FloatingSide/PlateClamp":{"_onCount":1,"_offCount":1,"_onTime":13.0}
        },"Date":"2026-09-08T01:00:00Z"}
        """;

    [Fact]
    public void NamesTheSideWhenTheTwoRanForDifferentTimes()
    {
        var findings = IoSideResolver.Resolve(
            IoTimeline.Build(MachineLogFile.Parse(SidesThatDiffer)),
            MaintenanceCounters.Parse(CountersThatDiffer));

        Assert.True(findings.WindowTrusted);
        Assert.Equal(MachineSide.FixedSide, findings.Resolved.Single(r => r.Id.Address == "1-4.4").Side);
        Assert.Equal(MachineSide.FloatingSide, findings.Resolved.Single(r => r.Id.Address == "1-4.5").Side);
        Assert.Empty(findings.Ambiguous);
    }

    /// <summary>
    /// The case that matters most. Both sides clamp and release together all hour, so their
    /// on-times are identical and nothing in the evidence says which address is which. The
    /// answer is to say so - guessing a side from a neighbouring pair is exactly the mistake
    /// that put the M21737 partner address two bits and a module away from the real one.
    /// </summary>
    [Fact]
    public void RefusesToChooseWhenBothSidesRanIdentically()
    {
        var log = new[]
        {
            "12:00:10.0000000,  OutputChange, IO-PlateSupport,  Output (1-1.2) Set On",
            "12:00:10.0000000,  OutputChange, IO-PlateSupport,  Output (1-1.13) Set On",
            "12:00:20.0000000,  OutputChange, IO-PlateSupport,  Output (1-1.2) Set Off",
            "12:00:20.0000000,  OutputChange, IO-PlateSupport,  Output (1-1.13) Set Off"
        };

        var counters = """
            {"TwoStates":{
              "FixedSide/PlateSupport":{"_onCount":1,"_offCount":1,"_onTime":10.0},
              "FloatingSide/PlateSupport":{"_onCount":1,"_offCount":1,"_onTime":10.0}
            }}
            """;

        var findings = IoSideResolver.Resolve(
            IoTimeline.Build(MachineLogFile.Parse(log)), MaintenanceCounters.Parse(counters));

        Assert.Empty(findings.Resolved);
        Assert.Equal(2, findings.Ambiguous.Count);
    }

    /// <summary>
    /// A shared point has one name, so its side comes from the name and no timing goes into it.
    /// That has to survive a counter file whose hour does not line up with the log - which is
    /// what the M21737 bundle actually has.
    /// </summary>
    [Fact]
    public void ANameWithNoPartnerIsTrustedEvenWhenTheWindowDoesNot()
    {
        var log = new[]
        {
            "12:00:10.0000000,  OutputChange, IO-RackLock,  Output (1-0.1) Set On",
            "12:00:20.0000000,  OutputChange, IO-RackLock,  Output (1-0.1) Set Off"
        };

        var counters = """{"TwoStates":{"CommonIO/RackLock":{"_onCount":9,"_offCount":9,"_onTime":987.6}}}""";

        var findings = IoSideResolver.Resolve(
            IoTimeline.Build(MachineLogFile.Parse(log)), MaintenanceCounters.Parse(counters));

        Assert.False(findings.WindowTrusted);
        var rack = Assert.Single(findings.Trustworthy);
        Assert.Equal(MachineSide.Shared, rack.Side);
        Assert.True(rack.FromNameAlone);
    }

    [Fact]
    public void NoCounterFileMeansNoFindings()
    {
        var findings = IoSideResolver.Resolve(
            IoTimeline.Build(MachineLogFile.Parse(SidesThatDiffer)), Array.Empty<OutputDuty>());

        Assert.False(findings.Any);
    }
}
