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

    /// <summary>
    /// Wall Sheather - 24 named points across 67 numbers, because most things are fitted three
    /// or five times rather than twice. Three bridge gun gantries and two bridge saws run over
    /// the panel, so IO-GunFire exists five times and GunUp five times.
    /// <para>
    /// Read from one M20957 log, 06:02 to 14:54 on 22 September 2026 at Trusstech, which the
    /// operator marked "good panel". One machine, one day - a short log, so this is what that
    /// day used and not everything the machine has.
    /// </para>
    /// <para>
    /// <b>Nails are not in the I/O.</b> The machine loads a firing pattern per gun - "LoadGunFire,
    /// Bridge1 Gun, Firstpos 138.75 Spacing 143.6765" - and then logs one FireSeqCompeted per
    /// row. A whole row of nails is one line. Counting IO-GunFire edges gives nothing like the
    /// nail count, so the count comes from Reports/LatestReport.txt instead.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<KnownSignal> WallSheather = new KnownSignal[]
    {
        new(SignalKind.Input, "GunNotRotated", 3, new[] { "2.6", "3.1", "3.12" }),
        new(SignalKind.Input, "GunRotated", 3, new[] { "2.7", "3.2", "3.13" }),
        new(SignalKind.Input, "GunUp", 5, new[] { "1.8", "2.0", "2.10", "3.5", "4.0" }),
        new(SignalKind.Input, "HorizStudClampDown", 2, new[] { "1.4", "1.12" }),
        new(SignalKind.Input, "LockedRotation", 3, new[] { "2.8", "3.3", "3.14" }),
        new(SignalKind.Input, "SawNotRotated", 2, new[] { "5.0", "5.5" }),
        new(SignalKind.Input, "SawRotationLocked", 2, new[] { "5.3", "5.8" }),
        new(SignalKind.Input, "StudPinDown", 2, new[] { "1.3", "1.11" }),
        new(SignalKind.Input, "StudPinUp", 2, new[] { "1.2", "1.10" }),
        new(SignalKind.Input, "UnlockedRotation", 3, new[] { "2.9", "3.4", "3.15" }),
        new(SignalKind.Input, "Up10mm", 3, new[] { "2.12", "3.7", "4.2" }),
        new(SignalKind.Output, "IO-GunAir", 1, new[] { "20.20" }),
        new(SignalKind.Output, "IO-GunDown", 5, new[] { "1.1", "1.6", "4.9", "5.9", "6.9" }),
        new(SignalKind.Output, "IO-GunFire", 5, new[] { "1.2", "1.7", "1.12", "1.13", "1.14" }),
        new(SignalKind.Output, "IO-GunNotRotate", 3, new[] { "4.1", "5.1", "6.1" }),
        new(SignalKind.Output, "IO-GunUp", 5, new[] { "1.0", "1.5", "4.8", "5.8", "6.8" }),
        new(SignalKind.Output, "IO-GunUpperDown", 1, new[] { "20.0" }),
        new(SignalKind.Output, "IO-GunUpperUp", 1, new[] { "20.0" }),
        new(SignalKind.Output, "IO-HorizStudClamp", 2, new[] { "1.4", "1.9" }),
        new(SignalKind.Output, "IO-Lift10mm", 3, new[] { "4.10", "5.10", "6.10" }),
        new(SignalKind.Output, "IO-RamLock", 3, new[] { "4.6", "5.6", "6.6" }),
        new(SignalKind.Output, "IO-RotateLock", 3, new[] { "4.4", "5.4", "6.4" }),
        new(SignalKind.Output, "IO-SawNotRotate", 2, new[] { "3.1", "3.3" }),
        new(SignalKind.Output, "IO-StudPinUp", 3, new[] { "1.3", "1.8", "20.0" }),
    };

    /// <summary>
    /// Component Nailer V2 - 33 named points, each fitted once. Front and back clamps are named
    /// separately rather than paired.
    /// <para>
    /// Read from the machine's own Diagnostics screens (September 2026), and the numbers checked
    /// against the M21868 logs: IO-VertFrontClamp at 0.8, IO-VertBackClamp at 0.7,
    /// UpperGunLowerIsHigh at 0.6, IO-UpperGunLowerGoHigh at 0.3.
    /// </para>
    /// <para>
    /// <b>Left out on purpose:</b> points not in use - HorizClampExtended and ThreePhaseOK
    /// (confirmed by support), and those the screen shows as Not In Use: LowerNailSensor, UpperNailSensor, PlateHeightOver85, UpperGunWoodSensor,
    /// LowerGunWoodSensor and the HorizClampBack output. They never change, so listing them would
    /// report every one as a sensor that never came on.
    /// </para>
    /// <para>
    /// THNTD and both clamp lock outputs are inverted on the screen. The four analog inputs
    /// (FixedSideHeight, NogDistaceFromTopOfPlate, AirPressure, ClampAirPressure) are not
    /// here because the log does not record them as input changes.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<KnownSignal> ComponentNailerV2 = new KnownSignal[]
    {
        new(SignalKind.Input, "THNTD", 1, new[] { "0.1" }),
        new(SignalKind.Input, "ProductSensor", 1, new[] { "0.2" }),
        new(SignalKind.Input, "UpperGunUpperIsLow", 1, new[] { "0.3" }),
        new(SignalKind.Input, "UpperGunUpperIsHigh", 1, new[] { "0.4" }),
        new(SignalKind.Input, "UpperGunLowerIsLow", 1, new[] { "0.5" }),
        new(SignalKind.Input, "UpperGunLowerIsHigh", 1, new[] { "0.6" }),
        new(SignalKind.Input, "FenceRetracted", 1, new[] { "0.7" }),
        new(SignalKind.Input, "HorizClampRetracted", 1, new[] { "0.8" }),
        new(SignalKind.Input, "VertClampBackExtended", 1, new[] { "0.9" }),
        new(SignalKind.Input, "VertClampBackRetracted", 1, new[] { "0.10" }),
        new(SignalKind.Input, "VertFrontClampExtended", 1, new[] { "0.11" }),
        new(SignalKind.Input, "VertFrontClampRetracted", 1, new[] { "0.12" }),
        new(SignalKind.Input, "TableUpRetracted", 1, new[] { "1.0" }),
        new(SignalKind.Input, "TableUpExtended", 1, new[] { "1.1" }),
        new(SignalKind.Input, "ClampLift5mmRetracted", 1, new[] { "1.2" }),
        new(SignalKind.Input, "ClampLift5mmExtended", 1, new[] { "1.3" }),
        new(SignalKind.Input, "GunAirOK", 1, new[] { "1.6" }),
        new(SignalKind.Input, "ClampAirOK", 1, new[] { "1.7" }),
        new(SignalKind.Output, "IO-LowerGunFire", 1, new[] { "0.0" }),
        new(SignalKind.Output, "IO-UpperGunFire", 1, new[] { "0.1" }),
        new(SignalKind.Output, "IO-THNTDLED", 1, new[] { "0.2" }),
        new(SignalKind.Output, "IO-UpperGunLowerGoHigh", 1, new[] { "0.3" }),
        new(SignalKind.Output, "IO-UpperGunUpperGoHigh", 1, new[] { "0.4" }),
        new(SignalKind.Output, "IO-FenceUp", 1, new[] { "0.5" }),
        new(SignalKind.Output, "IO-HorizClamp", 1, new[] { "0.6" }),
        new(SignalKind.Output, "IO-VertBackClamp", 1, new[] { "0.7" }),
        new(SignalKind.Output, "IO-VertFrontClamp", 1, new[] { "0.8" }),
        new(SignalKind.Output, "IO-VertTableUp", 1, new[] { "0.9" }),
        new(SignalKind.Output, "IO-VertBackClampLock", 1, new[] { "1.0" }),
        new(SignalKind.Output, "IO-VertFrontClampLock", 1, new[] { "1.1" }),
        new(SignalKind.Output, "IO-ClampLift5mm", 1, new[] { "1.2" }),
        new(SignalKind.Output, "IO-VertBackClampUp", 1, new[] { "1.3" }),
        new(SignalKind.Output, "IO-VertFrontClampUp", 1, new[] { "1.4" }),
    };

    private static readonly (string Prefix, IReadOnlyList<KnownSignal> Points)[] ByModel =
    {
        ("ComponentNailerV2", ComponentNailerV2),
        ("RakingWallExtruderV3", RakedWallExtruderV3),
        ("RakedWallExtruderV3", RakedWallExtruderV3),
        ("WallSheather", WallSheather),
        ("WallSheath", WallSheather)
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
