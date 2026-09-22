using DiagFileMonitor.Core.Knowledge;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Tests;

public class HomeSensorComplaintTests
{
    private static readonly DateTime Bundle = new(2026, 9, 20, 10, 0, 0);
    private static readonly IReadOnlyList<ChangeLogEntry> NoChanges = Array.Empty<ChangeLogEntry>();

    private static ConfiguredAxis Axis(string name, int node, string homeMode = "Sensor", string port = "192.168.250.1") =>
        new(name, node, port, 1000, 300, 1, homeMode);

    private static MachineConfig Config(params ConfiguredAxis[] axes) =>
        new(Array.Empty<ConfiguredSignal>(), axes, new[] { "192.168.250.1" });

    /// <summary>The V3's servos, node for node.</summary>
    private static readonly MachineConfig RakedV3 = Config(
        Axis("FixedSide Trolley", 0), Axis("FloatingSide Trolley", 1),
        Axis("FixedSide EjectServo", 2), Axis("FloatingSide EjectServo", 3),
        Axis("TrolleyHeight", 4), Axis("FloatingSide YAxis", 5));

    /// <summary>The Tornado M500: belts define their position, everything else homes to a sensor.</summary>
    private static readonly MachineConfig Tornado = Config(
        Axis("MainIO XInAxis", 0, "DefinePosition"), Axis("MainIO XOutAxis", 1, "DefinePosition"),
        Axis("FollowerAxis", 2), Axis("YZR YAxis", 3), Axis("YZR ZAxis", 4), Axis("YZR RAxis", 5));

    private static readonly MachineConfig WallSheather = Config(
        Axis("FixedSide Trolley", 0), Axis("FloatingSide Trolley", 1),
        Axis("BridgeGun1 Trolley", 2), Axis("BridgeGun2 Trolley", 3), Axis("BridgeGun3 Trolley", 4),
        Axis("BridgeSaw1 ZAxis", 5), Axis("PanelHeight", 6));

    private static ChangeLogEntry Change(string category, string setting, int daysAgo = 5) => new()
    {
        Timestamp = Bundle.AddDays(-daysAgo), Category = category, Setting = setting,
        OldValue = "100", NewValue = "130", User = "Admin"
    };

    [Fact]
    public void M21856_fixed_side_out_is_the_fixed_side_trolley()
    {
        var found = HomeSensorComplaintCheck.Check("fixe side out by 30mm", NoChanges, Bundle, "Raked Wall Extruder V3", RakedV3)!;

        Assert.Equal("fixed side trolley", found.Axis);
        Assert.Equal(30, found.OffsetMm);
        Assert.True(found.HomesToSensor);
        Assert.False(found.SettingsChanged);
        Assert.Empty(found.OtherCandidates);
    }

    [Fact]
    public void Without_a_config_a_side_still_means_its_trolley()
    {
        var found = HomeSensorComplaintCheck.Check("floating side is 12 mm out", NoChanges, Bundle, null)!;

        Assert.Equal("floating side trolley", found.Axis);
        Assert.Equal(12, found.OffsetMm);
        Assert.Null(found.HomesToSensor);
    }

    [Fact]
    public void Tornado_follower_homes_to_a_sensor()
    {
        var found = HomeSensorComplaintCheck.Check("infeed follower out by 20mm", NoChanges, Bundle, "Tornado M500", Tornado)!;

        Assert.Equal("follower axis", found.Axis);
        Assert.True(found.HomesToSensor);
    }

    [Fact]
    public void Tornado_in_belt_defines_its_position_so_no_sensor_advice()
    {
        var found = HomeSensorComplaintCheck.Check("in belt 15mm out", NoChanges, Bundle, "Tornado M500", Tornado)!;

        Assert.Equal("in belt (XInAxis)", found.Axis);
        Assert.False(found.HomesToSensor);
        Assert.Equal("DefinePosition", found.HomeMode);
    }

    [Fact]
    public void Out_by_does_not_match_the_out_belt()
    {
        var found = HomeSensorComplaintCheck.Check("saw height out by 5mm", NoChanges, Bundle, "Tornado M500", Tornado)!;

        Assert.Equal("saw Z axis", found.Axis);
    }

    [Fact]
    public void Wall_sheather_bridge_gun_by_number()
    {
        var found = HomeSensorComplaintCheck.Check("bridge gun 2 off by 10mm", NoChanges, Bundle, "Wall Sheather", WallSheather)!;

        Assert.Equal("bridge gun 2 trolley", found.Axis);
        Assert.True(found.HomesToSensor);
    }

    [Fact]
    public void No_servo_named_lists_the_sensor_homed_ones()
    {
        var found = HomeSensorComplaintCheck.Check("panels are out by 8mm", NoChanges, Bundle, "Tornado M500", Tornado)!;

        Assert.False(found.AxisKnown);
        Assert.Contains("follower axis", found.SensorHomedAxes);
        Assert.DoesNotContain("in belt (XInAxis)", found.SensorHomedAxes);
    }

    [Fact]
    public void No_amount_means_no_finding()
    {
        Assert.Null(HomeSensorComplaintCheck.Check("fixed side gripper not clamping", NoChanges, Bundle, null, RakedV3));
        Assert.Null(HomeSensorComplaintCheck.Check("", NoChanges, Bundle, null, RakedV3));
    }

    [Fact]
    public void A_recent_home_position_change_on_that_axis_is_listed_first()
    {
        var changes = new[]
        {
            // Change.log's own name for the fixed side trolley, as on M20771.
            Change("FixedSidePuller", "HomePosition"),
            Change("FloatingSidePuller", "HomePosition"),
            Change("TrolleyHeight", "HomePosition"),
            Change("FixedSidePuller", "Velocity"),
            Change("FixedSidePuller", "Scale", daysAgo: 400)
        };

        var found = HomeSensorComplaintCheck.Check("fixed side out by 30mm", changes, Bundle, null, RakedV3)!;

        Assert.True(found.SettingsChanged);
        var only = Assert.Single(found.RelevantChanges);
        Assert.Equal("FixedSidePuller", only.Category);
        Assert.Equal("HomePosition", only.Setting);
    }

    [Fact]
    public void Report_names_the_axis_and_points_at_the_photos()
    {
        var complaint = ComplaintRouter.Route("fixe side out by 30mm", NoChanges, Array.Empty<MachineLogEntry>(), "Raked Wall Extruder V3", Bundle, RakedV3);

        Assert.NotNull(complaint.HomeSensor);
        Assert.Contains(complaint.Guides, g => g.Id == "home-sensor-gap");
    }
}
