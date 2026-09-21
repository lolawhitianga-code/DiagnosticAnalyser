using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// Which half of the machine a point belongs to. Most points come in fixed/floating pairs, a
/// few are shared, and for the rest nothing in the evidence says which is which.
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

/// <summary>One I/O point a machine is known to have.</summary>
public record KnownSignal(
    SignalKind Kind,
    string Name,
    string Address,
    int ChangesSeen,
    string PartnerAddress,
    MachineSide Side = MachineSide.Unknown)
{
    /// <summary>These machines do almost everything twice, one per side.</summary>
    public bool IsPaired => PartnerAddress.Length > 0;

    public SignalId Id => new(Kind, Name, Address);

    /// <summary>How to name this point to a technician standing at the machine.</summary>
    public string Describe() => Side switch
    {
        MachineSide.FixedSide => $"{Name} (fixed side, {Address})",
        MachineSide.FloatingSide => $"{Name} (floating side, {Address})",
        MachineSide.Shared => $"{Name} (shared, {Address})",
        _ => $"{Name} ({Address})"
    };
}

/// <summary>
/// The I/O points a model is known to have, so a single short log can be read against the whole
/// machine rather than against the handful of points that happened to move in it.
/// <para>
/// This is the gap that cost us the M21737 case. A log records changes, so an input that never
/// came on never appears - and with nothing to compare against, its absence is invisible. Nothing
/// in a bundle carries an I/O map: Machine.xml has none, and neither does any of the model XMLs.
/// </para>
/// <para>
/// So it is learned. <see cref="SignalCatalogueService"/> accumulates it per serial from every
/// bundle that arrives; this is the same thing per model, seeded from a log long enough to have
/// exercised the whole machine.
/// </para>
/// </summary>
public static class MachineIoMap
{
    /// <summary>
    /// Raked Wall Extruder V3, read from two logs: an 82,149 line M21737 log covering 04:07 to
    /// 13:51 on 27 July 2026, and an 84,360 line M21844 log covering 04:32 to 12:50 on
    /// 8 September 2026. Every point found on the first machine appears on the second at the same
    /// address, which is the only reason this is worth relying on at all.
    /// <para>
    /// <b>Still inferred, not confirmed.</b> Two machines, two logs, no wiring diagram. A point
    /// neither machine used on its day is not in the list.
    /// </para>
    /// <para>
    /// <b>Sides.</b> Fifteen points carry a side; the rest do not, and an Unknown side means
    /// exactly that. Where a side is set it was measured, not guessed, by one of two methods:
    /// </para>
    /// <list type="bullet">
    /// <item>The inputs at 4.2 and 4.4 are settled by the machine's own words. Its "Lost product,
    /// revert and try again. Fixed Product: X Floating: Y" message names the side that lost the
    /// plate, and across 25 of those messages the side it named was the one whose address was
    /// reading 0 - 4.2 every time it said the fixed side, 4.4 for the floating side, and both when
    /// it said both. 25 out of 25.</item>
    /// <item>The outputs are settled by CloudLog/maint_data.json, which turns out to carry the
    /// current hour's on-count and on-time for every output under its real name - FixedSide/...,
    /// FloatingSide/..., CommonIO/... Matching those durations back to the addresses in the log
    /// names the side outright, to within a few hundredths of a second. It only separates a pair
    /// whose two sides actually ran for different lengths of time, which is why eleven pairs are
    /// still Unknown: their two halves moved together all hour and are indistinguishable.</item>
    /// </list>
    /// <para>
    /// Note that the sides do not follow a rule. Lower-bit-is-fixed holds for the gripper, plate
    /// clamp and upper gripper pairs in module 4 and then breaks on the horizontal stud clamp,
    /// where 4.12 is the floating side and 4.13 the fixed. Extrapolating a side from a
    /// neighbouring pair is how the M21737 partner address was got wrong; read it from
    /// maint_data.json instead, per <see cref="IoSideResolver"/>.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<KnownSignal> RakedWallExtruderV3 = new KnownSignal[]
    {
        new(SignalKind.Input, "ResetPB", "192.168.250.1-0.4", 28, ""),
        new(SignalKind.Input, "TrolleyBottomClampOpen", "192.168.250.1-0.6", 1, "192.168.250.1-1.4"),
        new(SignalKind.Input, "TrolleyBottomClampClosed", "192.168.250.1-0.7", 167, "192.168.250.1-1.5"),
        new(SignalKind.Input, "TrolleyTopClampOpen", "192.168.250.1-0.8", 139, "192.168.250.1-1.6"),
        new(SignalKind.Input, "PlateClampUp", "192.168.250.1-0.10", 157, "192.168.250.1-1.8"),
        new(SignalKind.Input, "PlateClampLifted10mm", "192.168.250.1-0.12", 909, "192.168.250.1-1.10"),
        new(SignalKind.Input, "TopStudClampUp", "192.168.250.1-0.13", 915, "192.168.250.1-1.11"),
        new(SignalKind.Input, "HorizStudClampDown", "192.168.250.1-0.15", 791, "192.168.250.1-1.13"),
        new(SignalKind.Input, "StudPinDown", "192.168.250.1-0.16", 649, "192.168.250.1-1.14"),
        new(SignalKind.Input, "StudPin2Down", "192.168.250.1-0.17", 155, "192.168.250.1-1.15"),
        new(SignalKind.Input, "UpperGunUpperIsLow", "192.168.250.1-0.19", 1, "192.168.250.1-2.1"),
        new(SignalKind.Input, "UpperGunLowerIsLow", "192.168.250.1-0.21", 1, "192.168.250.1-2.3"),
        new(SignalKind.Input, "PlateSupportUp", "192.168.250.1-1.0", 151, "192.168.250.1-2.6"),
        new(SignalKind.Input, "PlateSupportDown", "192.168.250.1-1.1", 139, "192.168.250.1-2.7"),
        new(SignalKind.Input, "SideClampOff", "192.168.250.1-1.3", 1, ""),
        new(SignalKind.Input, "TrolleyBottomClampOpen", "192.168.250.1-1.4", 1, "192.168.250.1-0.6"),
        new(SignalKind.Input, "TrolleyBottomClampClosed", "192.168.250.1-1.5", 167, "192.168.250.1-0.7"),
        new(SignalKind.Input, "TrolleyTopClampOpen", "192.168.250.1-1.6", 139, "192.168.250.1-0.8"),
        new(SignalKind.Input, "PlateClampUp", "192.168.250.1-1.8", 156, "192.168.250.1-0.10"),
        new(SignalKind.Input, "PlateClampLifted10mm", "192.168.250.1-1.10", 905, "192.168.250.1-0.12"),
        new(SignalKind.Input, "TopStudClampUp", "192.168.250.1-1.11", 907, "192.168.250.1-0.13"),
        new(SignalKind.Input, "HorizStudClampDown", "192.168.250.1-1.13", 769, "192.168.250.1-0.15"),
        new(SignalKind.Input, "StudPinDown", "192.168.250.1-1.14", 629, "192.168.250.1-0.16"),
        new(SignalKind.Input, "StudPin2Down", "192.168.250.1-1.15", 157, "192.168.250.1-0.17"),
        new(SignalKind.Input, "UpperGunUpperIsLow", "192.168.250.1-2.1", 1, "192.168.250.1-0.19"),
        new(SignalKind.Input, "UpperGunLowerIsLow", "192.168.250.1-2.3", 1, "192.168.250.1-0.21"),
        new(SignalKind.Input, "PlateSupportUp", "192.168.250.1-2.6", 151, "192.168.250.1-1.0"),
        new(SignalKind.Input, "PlateSupportDown", "192.168.250.1-2.7", 1, "192.168.250.1-1.1"),
        new(SignalKind.Input, "RackLockOff", "192.168.250.1-2.9", 1156, ""),
        new(SignalKind.Input, "GunAirOK", "192.168.250.1-2.10", 1, ""),
        new(SignalKind.Input, "ClampsAirOK", "192.168.250.1-2.11", 1, ""),
        new(SignalKind.Input, "EStop", "192.168.250.1-4.0", 1, ""),
        new(SignalKind.Input, "THNTD", "192.168.250.1-4.1", 1624, ""),
        new(SignalKind.Input, "PlatePresentSwitch", "192.168.250.1-4.2", 151, "192.168.250.1-4.4", MachineSide.FixedSide),
        new(SignalKind.Input, "PlatePresentSwitch", "192.168.250.1-4.4", 151, "192.168.250.1-4.2", MachineSide.FloatingSide),
        new(SignalKind.Input, "GripperProductSensor", "192.168.250.1-4.6", 163, "192.168.250.1-4.7"),
        new(SignalKind.Input, "GripperProductSensor", "192.168.250.1-4.7", 163, "192.168.250.1-4.6"),
        new(SignalKind.Input, "SafetyBarPressed", "192.168.250.1-4.8", 7, ""),
        new(SignalKind.Input, "EstopResetButton", "192.168.250.1-4.9", 10, ""),
        new(SignalKind.Output, "IO-PlatePresentBypass", "192.168.250.1-0.0", 171, "", MachineSide.FixedSide),
        new(SignalKind.Output, "IO-RackLock", "192.168.250.1-0.1", 1284, "", MachineSide.Shared),
        new(SignalKind.Output, "IO-FireLamp", "192.168.250.1-0.4", 1337, "192.168.250.1-0.7"),
        new(SignalKind.Output, "IO-ResetLamp", "192.168.250.1-0.5", 229, "192.168.250.1-0.8"),
        new(SignalKind.Output, "IO-ClampedFireReleaseLamp", "192.168.250.1-0.6", 2518, "192.168.250.1-0.9"),
        new(SignalKind.Output, "IO-FireLamp", "192.168.250.1-0.7", 1337, "192.168.250.1-0.4"),
        new(SignalKind.Output, "IO-ResetLamp", "192.168.250.1-0.8", 229, "192.168.250.1-0.5"),
        new(SignalKind.Output, "IO-ClampedFireReleaseLamp", "192.168.250.1-0.9", 2518, "192.168.250.1-0.6"),
        new(SignalKind.Output, "IO-EstopResetLamp", "192.168.250.1-0.12", 2, ""),
        new(SignalKind.Output, "IO-StudPinUp", "192.168.250.1-0.13", 654, "192.168.250.1-1.8"),
        new(SignalKind.Output, "IO-StudPinUp2", "192.168.250.1-0.14", 160, "192.168.250.1-1.9", MachineSide.FixedSide),
        new(SignalKind.Output, "IO-PlateClampLock", "192.168.250.1-1.0", 907, "192.168.250.1-1.11"),
        new(SignalKind.Output, "IO-PlateClampLift10mm", "192.168.250.1-1.1", 915, "192.168.250.1-1.12"),
        new(SignalKind.Output, "IO-PlateSupport", "192.168.250.1-1.2", 159, "192.168.250.1-1.13"),
        new(SignalKind.Output, "IO-SideClamp", "192.168.250.1-1.5", 852, "", MachineSide.Shared),
        new(SignalKind.Output, "IO-StudPinUp", "192.168.250.1-1.8", 630, "192.168.250.1-0.13"),
        new(SignalKind.Output, "IO-StudPinUp2", "192.168.250.1-1.9", 162, "192.168.250.1-0.14", MachineSide.FloatingSide),
        new(SignalKind.Output, "IO-PlateClampLock", "192.168.250.1-1.11", 901, "192.168.250.1-1.0"),
        new(SignalKind.Output, "IO-PlateClampLift10mm", "192.168.250.1-1.12", 905, "192.168.250.1-1.1"),
        new(SignalKind.Output, "IO-PlateSupport", "192.168.250.1-1.13", 159, "192.168.250.1-1.2"),
        new(SignalKind.Output, "IO-LowerGunFire", "192.168.250.1-4.0", 1152, "192.168.250.1-4.2"),
        new(SignalKind.Output, "IO-UpperGunFire", "192.168.250.1-4.1", 1152, "192.168.250.1-4.3"),
        new(SignalKind.Output, "IO-LowerGunFire", "192.168.250.1-4.2", 1142, "192.168.250.1-4.0"),
        new(SignalKind.Output, "IO-UpperGunFire", "192.168.250.1-4.3", 1142, "192.168.250.1-4.1"),
        new(SignalKind.Output, "IO-PlateClamp", "192.168.250.1-4.4", 914, "192.168.250.1-4.5", MachineSide.FixedSide),
        new(SignalKind.Output, "IO-PlateClamp", "192.168.250.1-4.5", 904, "192.168.250.1-4.4", MachineSide.FloatingSide),
        new(SignalKind.Output, "IO-PlateClampUp", "192.168.250.1-4.6", 160, "192.168.250.1-4.7"),
        new(SignalKind.Output, "IO-PlateClampUp", "192.168.250.1-4.7", 156, "192.168.250.1-4.6"),
        new(SignalKind.Output, "IO-UpperGripper", "192.168.250.1-4.8", 163, "192.168.250.1-4.9", MachineSide.FixedSide),
        new(SignalKind.Output, "IO-UpperGripper", "192.168.250.1-4.9", 163, "192.168.250.1-4.8", MachineSide.FloatingSide),
        new(SignalKind.Output, "IO-LowerGripper", "192.168.250.1-4.10", 167, "192.168.250.1-4.11", MachineSide.FixedSide),
        new(SignalKind.Output, "IO-LowerGripper", "192.168.250.1-4.11", 167, "192.168.250.1-4.10", MachineSide.FloatingSide),
        new(SignalKind.Output, "IO-HorizStudClamp", "192.168.250.1-4.12", 780, "192.168.250.1-4.13", MachineSide.FloatingSide),
        new(SignalKind.Output, "IO-HorizStudClamp", "192.168.250.1-4.13", 804, "192.168.250.1-4.12", MachineSide.FixedSide),
        new(SignalKind.Output, "IO-TopStudClamp", "192.168.250.1-4.14", 922, "192.168.250.1-4.15"),
        new(SignalKind.Output, "IO-TopStudClamp", "192.168.250.1-4.15", 914, "192.168.250.1-4.14"),

        // Eight points the M21737 log never exercised, added from an 84,360 line M21844 log
        // covering 04:32 to 12:50 on 8 September 2026. Every one of the 75 points above appears
        // in that log at the same address, so the map now rests on two machines rather than one.
        new(SignalKind.Input, "Control Box 1", "192.168.250.1-0.1", 8, ""),
        new(SignalKind.Input, "UpperGunLowerIsHigh", "192.168.250.1-0.20", 73, "192.168.250.1-2.2"),
        new(SignalKind.Input, "UpperGunLowerIsHigh", "192.168.250.1-2.2", 73, "192.168.250.1-0.20"),
        new(SignalKind.Input, "THNTD", "192.168.250.1-4.3", 8, "192.168.250.1-4.1"),
        new(SignalKind.Output, "IO-UpperGunLowerGoHigh", "192.168.250.1-0.2", 76, "192.168.250.1-1.6"),
        new(SignalKind.Output, "IO-UpperGunLowerGoHigh", "192.168.250.1-1.6", 76, "192.168.250.1-0.2"),
        new(SignalKind.Output, "IO-UpperGunUpperGoHigh", "192.168.250.1-0.3", 2, "192.168.250.1-1.7"),
        new(SignalKind.Output, "IO-UpperGunUpperGoHigh", "192.168.250.1-1.7", 2, "192.168.250.1-0.3"),
    };

    /// <summary>Models are matched as a prefix, so RakingWallExtruderV3DG finds the V3 list.</summary>
    private static readonly (string Prefix, IReadOnlyList<KnownSignal> Points)[] ByModel =
    {
        ("RakingWallExtruderV3", RakedWallExtruderV3),
        ("RakedWallExtruderV3", RakedWallExtruderV3)
    };

    /// <summary>What we know this model has, or nothing where we have never mapped one.</summary>
    public static IReadOnlyList<KnownSignal> For(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return Array.Empty<KnownSignal>();

        foreach (var (prefix, points) in ByModel)
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return points;

        return Array.Empty<KnownSignal>();
    }

    /// <summary>
    /// The addresses this model uses for a named signal - both halves where it is paired.
    /// </summary>
    public static IReadOnlyList<KnownSignal> Find(string? model, SignalKind kind, string name) =>
        For(model)
            .Where(s => s.Kind == kind && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
