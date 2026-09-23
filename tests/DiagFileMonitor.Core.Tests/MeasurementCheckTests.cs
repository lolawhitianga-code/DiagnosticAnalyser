using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// M21868, 17 September 2026: "clamp wont engage and clamp nog". The nog clamp was coming down
/// very slowly from about 200 mm and was locked before it reached 45 mm. The log said so 41 times.
/// </summary>
public class MeasurementCheckTests
{
    private static readonly string[] GoodCycle =
    {
        "10:52:45.2342930,  OutputChange, IO-VertBackClamp,  Output (192.168.250.1-0.7) Set On",
        "10:52:45.2342930,  OutputChange, IO-VertFrontClampUp,  Output (192.168.250.1-1.4) Set Off",
        "10:52:45.2342930,  OutputChange, IO-VertFrontClamp,  Output (192.168.250.1-0.8) Set On",
        "10:52:46.3388036,  OutputChange, IO-HorizClamp,  Output (192.168.250.1-0.6) Set Off",
        "10:52:46.4781732,  OutputChange, IO-VertBackClampLock,  Output (192.168.250.1-1.0) Set On",
        "10:52:46.4791911,  OutputChange, IO-VertFrontClampLock,  Output (192.168.250.1-1.1) Set On"
    };

    private static readonly string[] FailedCycle =
    {
        "10:43:17.5608583,  OutputChange, IO-VertBackClampUp,  Output (192.168.250.1-1.3) Set Off",
        "10:43:17.5608583,  OutputChange, IO-VertBackClamp,  Output (192.168.250.1-0.7) Set On",
        "10:43:17.5608583,  OutputChange, IO-VertFrontClamp,  Output (192.168.250.1-0.8) Set On",
        "10:43:20.1851802,  Other, Nog Height,  Incorrect Nog Height From Top Of Stud, Expected : 45.0 Got: 12.2",
        "10:43:20.1851802,  Other, ComponentNailerV2PLC,  Step Condition, Waiting for Waiting For Operator to Resolve Assembly Nog Location (Nog Height) Issue",
        "10:43:20.1851802,  Other, ComponentNailerV2PLC,  Incorrect Nog Height From Top Of Stud, Expected : 45.0 Got: 12.2",
        "10:43:20.3248314,  Other, ComponentNailerV2,  Incorrect Nog Height"
    };

    private static MeasurementFindings Check(IEnumerable<ChangeLogEntry>? changes, params string[][] parts) =>
        MeasurementCheck.Check(MachineLogFile.Parse(parts.SelectMany(p => p).ToArray()), changes?.ToList());

    [Fact]
    public void Reads_the_failure_once_though_it_is_written_twice()
    {
        var check = Assert.Single(Check(null, FailedCycle).Failed);

        Assert.Equal("Incorrect Nog Height From Top Of Stud", check.What);
        Assert.Equal(1, check.Count);
        Assert.Equal(45, check.Expected);
        Assert.Equal(12.2, check.LowestGot);
        Assert.True(check.AllShort);
    }

    [Fact]
    public void Times_the_check_from_the_clamps_going_down_against_a_good_lock()
    {
        var found = Check(null, GoodCycle, FailedCycle);

        Assert.Equal(1, found.GoodClampCycles);
        Assert.Equal(1.24, found.TypicalClampToLock!.Value.TotalSeconds, 2);
        Assert.Equal(2.62, found.Failed[0].TypicalSinceClampDown!.Value.TotalSeconds, 2);
    }

    [Fact]
    public void Nog_height_carries_what_it_turned_out_to_be()
    {
        var check = Assert.Single(Check(null, FailedCycle).Failed);

        Assert.Contains("locked in place before it reached 45 mm", check.SeenBefore);
        Assert.Contains("flow control", check.SeenBefore);
    }

    [Fact]
    public void Lists_clamp_and_lock_timing_changes_only()
    {
        var at = new DateTime(2026, 6, 11, 7, 38, 54);
        var changes = new[]
        {
            new ChangeLogEntry { Timestamp = at, Setting = "LockDelay", OldValue = "1000", NewValue = "200" },
            new ChangeLogEntry { Timestamp = at, Setting = "ClampDelay", OldValue = "200", NewValue = "300" },
            new ChangeLogEntry { Timestamp = at, Setting = "GunFireTime", OldValue = "150", NewValue = "200" }
        };

        var found = Check(changes, FailedCycle);

        Assert.Equal(2, found.ClampTimingChanges.Count);
        Assert.DoesNotContain(found.ClampTimingChanges, c => c.Setting == "GunFireTime");
    }

    [Fact]
    public void Nothing_failed_means_nothing_to_say()
    {
        Assert.False(Check(null, GoodCycle).Any);
    }
}
