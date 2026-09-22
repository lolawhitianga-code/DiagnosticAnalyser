using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// Which half of the machine a point belongs to. Most come in fixed/floating pairs, a few are
/// shared, and for the rest nothing in the evidence says which is which.
/// </summary>
public enum MachineSide
{
    /// <summary>No evidence either way. Do not guess in front of a customer.</summary>
    Unknown,
    FixedSide,
    FloatingSide,
    /// <summary>One point serving the whole machine - a rack lock, a side clamp, a lamp.</summary>
    Shared
}

/// <summary>
/// One thing a machine has, by name.
/// <para>
/// The name is the identity. The numbers are not: two machines of one model do not agree on
/// their I/O numbering - some Wall Extruder DGs run a point on node 5 and some on node 6, and on
/// three Raked Wall Extruder V3s <c>UpperGunUpperIsLow</c> sits at 0.19 on two of them and 0.18
/// on the third. So <see cref="PointsSeen"/> is what the mapped machines happened to use, for
/// reference, and the number that matters is the one in the log in front of you.
/// </para>
/// </summary>
public record KnownSignal(
    SignalKind Kind,
    string Name,
    int Instances,
    IReadOnlyList<string> PointsSeen,
    IReadOnlyList<MachineSide>? Sides = null)
{
    /// <summary>These machines do almost everything twice, one per side.</summary>
    public bool IsPaired => Instances > 1;

    /// <summary>The side of the nth instance, where it is known.</summary>
    public MachineSide SideOf(int index) =>
        Sides is not null && index >= 0 && index < Sides.Count ? Sides[index] : MachineSide.Unknown;

    public MachineSide SideAtPoint(string point)
    {
        var at = PointsSeen.ToList().FindIndex(p => p.Equals(point, StringComparison.OrdinalIgnoreCase));
        return at < 0 ? MachineSide.Unknown : SideOf(at);
    }

    /// <summary>How to say this to a technician standing at the machine.</summary>
    public string Describe(string? pointHere = null)
    {
        var where = pointHere is null ? string.Empty : $" at {pointHere}";
        var side = pointHere is null ? MachineSide.Unknown : SideAtPoint(pointHere);

        return side switch
        {
            MachineSide.FixedSide => $"{Name} (fixed side){where}",
            MachineSide.FloatingSide => $"{Name} (floating side){where}",
            MachineSide.Shared => $"{Name} (shared){where}",
            _ => $"{Name}{where}"
        };
    }
}

/// <summary>
/// What a model is known to have, <b>by name</b>.
/// <para>
/// This started as a list of addresses and that was the wrong shape. A log records changes, so a
/// point that never moved never appears and its absence is invisible - that is the gap worth
/// closing, and it is a gap in the <i>names</i>. The numbers are per machine: support's words
/// were "the actual number of the IO is less important than the name of the IO".
/// </para>
/// <para>
/// So this answers "what does a Raked Wall Extruder V3 have", and the log answers "and what is it
/// numbered on this one".
/// </para>
/// </summary>
public static class MachineIoMap
{
    /// <summary>
    /// Raked Wall Extruder V3 - 48 named points, 35 of them fitted twice, one per side.
    /// <para>
    /// Read from three machines: M21737 (82,149 lines), M21844 (84,360) and M20771 (eleven
    /// exports across nineteen months). All three agree on every name. They do not entirely
    /// agree on the numbering, which is why the numbering is not what this list is for.
    /// </para>
    /// <para>
    /// <b>Sides</b> are set on fifteen points only, and were measured rather than guessed - from
    /// the machine's own "Fixed Product: False" messages for the plate sensors, and from the
    /// per-output run times in CloudLog/maint_data.json for the rest. An Unknown side means
    /// exactly that; the two halves ran identically and nothing separates them.
    /// </para>
    /// <para>
    /// M20771 also carries an infeed, lifter and unloader subsystem - bay stops, chains, rollers
    /// and prox sensors, with its own MajorSubInfeedStep and MajorSubLifterStep sequencers - that
    /// the other two do not. Options differ per machine, so this list is never complete for any
    /// one of them.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<KnownSignal> RakedWallExtruderV3 = new KnownSignal[]
    {
        new(SignalKind.Input, "ClampsAirOK", 1, new[] { "2.11" }),
        new(SignalKind.Input, "Control Box 1", 1, new[] { "0.1" }),
        new(SignalKind.Input, "EStop", 1, new[] { "4.0" }),
        new(SignalKind.Input, "EstopResetButton", 1, new[] { "4.9" }),
        new(SignalKind.Input, "GripperProductSensor", 2, new[] { "4.6", "4.7" }),
        new(SignalKind.Input, "GunAirOK", 1, new[] { "2.10" }),
        new(SignalKind.Input, "HorizStudClampDown", 2, new[] { "0.15", "1.13" }),
        new(SignalKind.Input, "PlateClampLifted10mm", 2, new[] { "0.12", "1.10" }),
        new(SignalKind.Input, "PlateClampUp", 2, new[] { "0.10", "1.8" }),
        new(SignalKind.Input, "PlatePresentSwitch", 2, new[] { "4.2", "4.4" }, new[] { MachineSide.FixedSide, MachineSide.FloatingSide }),
        new(SignalKind.Input, "PlateSupportDown", 2, new[] { "1.1", "2.7" }),
        new(SignalKind.Input, "PlateSupportUp", 2, new[] { "1.0", "2.6" }),
        new(SignalKind.Input, "RackLockOff", 1, new[] { "2.9" }),
        new(SignalKind.Input, "ResetPB", 1, new[] { "0.4" }),
        new(SignalKind.Input, "SafetyBarPressed", 1, new[] { "4.8" }),
        new(SignalKind.Input, "SideClampOff", 1, new[] { "1.3" }),
        new(SignalKind.Input, "StudPin2Down", 2, new[] { "0.17", "1.15" }),
        new(SignalKind.Input, "StudPinDown", 2, new[] { "0.16", "1.14" }),
        new(SignalKind.Input, "THNTD", 2, new[] { "4.1", "4.3" }),
        new(SignalKind.Input, "TopStudClampUp", 2, new[] { "0.13", "1.11" }),
        new(SignalKind.Input, "TrolleyBottomClampClosed", 2, new[] { "0.7", "1.5" }),
        new(SignalKind.Input, "TrolleyBottomClampOpen", 2, new[] { "0.6", "1.4" }),
        new(SignalKind.Input, "TrolleyTopClampOpen", 2, new[] { "0.8", "1.6" }),
        new(SignalKind.Input, "UpperGunLowerIsHigh", 2, new[] { "0.20", "2.2" }),
        new(SignalKind.Input, "UpperGunLowerIsLow", 2, new[] { "0.21", "2.3" }),
        new(SignalKind.Input, "UpperGunUpperIsLow", 2, new[] { "0.19", "2.1" }),
        new(SignalKind.Output, "IO-ClampedFireReleaseLamp", 2, new[] { "0.6", "0.9" }),
        new(SignalKind.Output, "IO-EstopResetLamp", 1, new[] { "0.12" }),
        new(SignalKind.Output, "IO-FireLamp", 2, new[] { "0.4", "0.7" }),
        new(SignalKind.Output, "IO-HorizStudClamp", 2, new[] { "4.12", "4.13" }, new[] { MachineSide.FloatingSide, MachineSide.FixedSide }),
        new(SignalKind.Output, "IO-LowerGripper", 2, new[] { "4.10", "4.11" }, new[] { MachineSide.FixedSide, MachineSide.FloatingSide }),
        new(SignalKind.Output, "IO-LowerGunFire", 2, new[] { "4.0", "4.2" }),
        new(SignalKind.Output, "IO-PlateClamp", 2, new[] { "4.4", "4.5" }, new[] { MachineSide.FixedSide, MachineSide.FloatingSide }),
        new(SignalKind.Output, "IO-PlateClampLift10mm", 2, new[] { "1.1", "1.12" }),
        new(SignalKind.Output, "IO-PlateClampLock", 2, new[] { "1.0", "1.11" }),
        new(SignalKind.Output, "IO-PlateClampUp", 2, new[] { "4.6", "4.7" }),
        new(SignalKind.Output, "IO-PlatePresentBypass", 1, new[] { "0.0" }, new[] { MachineSide.FixedSide }),
        new(SignalKind.Output, "IO-PlateSupport", 2, new[] { "1.2", "1.13" }),
        new(SignalKind.Output, "IO-RackLock", 1, new[] { "0.1" }, new[] { MachineSide.Shared }),
        new(SignalKind.Output, "IO-ResetLamp", 2, new[] { "0.5", "0.8" }),
        new(SignalKind.Output, "IO-SideClamp", 1, new[] { "1.5" }, new[] { MachineSide.Shared }),
        new(SignalKind.Output, "IO-StudPinUp", 2, new[] { "0.13", "1.8" }),
        new(SignalKind.Output, "IO-StudPinUp2", 2, new[] { "0.14", "1.9" }, new[] { MachineSide.FixedSide, MachineSide.FloatingSide }),
        new(SignalKind.Output, "IO-TopStudClamp", 2, new[] { "4.14", "4.15" }),
        new(SignalKind.Output, "IO-UpperGripper", 2, new[] { "4.8", "4.9" }, new[] { MachineSide.FixedSide, MachineSide.FloatingSide }),
        new(SignalKind.Output, "IO-UpperGunFire", 2, new[] { "4.1", "4.3" }),
        new(SignalKind.Output, "IO-UpperGunLowerGoHigh", 2, new[] { "0.2", "1.6" }),
        new(SignalKind.Output, "IO-UpperGunUpperGoHigh", 2, new[] { "0.3", "1.7" }),
    };

    private static readonly (string Prefix, IReadOnlyList<KnownSignal> Points)[] ByModel =
    {
        ("RakingWallExtruderV3", RakedWallExtruderV3),
        ("RakedWallExtruderV3", RakedWallExtruderV3)
    };

    /// <summary>
    /// What we know this model has. Not gated on the control platform: the names are the same on
    /// the CLX and the Omron build, and it is only the numbering that differs - which this list
    /// does not claim to know.
    /// </summary>
    public static IReadOnlyList<KnownSignal> For(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return Array.Empty<KnownSignal>();

        foreach (var (prefix, points) in ByModel)
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return points;

        return Array.Empty<KnownSignal>();
    }

    /// <summary>One named point on a model, or nothing.</summary>
    public static KnownSignal? Find(string? model, SignalKind kind, string name) =>
        For(model).FirstOrDefault(s => s.Kind == kind && SameName(s.Name, name));

    /// <summary>
    /// Names differ in spacing and case between machines - one writes "E Stop" and another
    /// "Estop" for the same input - and they are not two different things.
    /// </summary>
    public static bool SameName(string a, string b) => Flatten(a) == Flatten(b);

    public static string Flatten(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
