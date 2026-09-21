using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One I/O point a machine is known to have.</summary>
public record KnownSignal(
    SignalKind Kind,
    string Name,
    string Address,
    int ChangesSeen,
    string PartnerAddress)
{
    /// <summary>These machines do almost everything twice, one per side.</summary>
    public bool IsPaired => PartnerAddress.Length > 0;

    public SignalId Id => new(Kind, Name, Address);
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
    /// Raked Wall Extruder V3, read from an 82,149 line M21737 log covering 04:07 to 13:51 on
    /// 27 July 2026 - nine and a half hours of production, 38,447 I/O changes.
    /// <para>
    /// <b>Inferred, not confirmed.</b> One machine, one log. A point this machine never used that
    /// day is not in the list, and the list has not been checked against a wiring diagram.
    /// </para>
    /// <para>
    /// <b>Which physical side each half of a pair is, is NOT known.</b> Both sides act within the
    /// same millisecond, so nothing in the log attributes an address to the floating or the fixed
    /// side. Anything claiming to is guessing.
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
        new(SignalKind.Input, "PlatePresentSwitch", "192.168.250.1-4.2", 151, "192.168.250.1-4.4"),
        new(SignalKind.Input, "PlatePresentSwitch", "192.168.250.1-4.4", 151, "192.168.250.1-4.2"),
        new(SignalKind.Input, "GripperProductSensor", "192.168.250.1-4.6", 163, "192.168.250.1-4.7"),
        new(SignalKind.Input, "GripperProductSensor", "192.168.250.1-4.7", 163, "192.168.250.1-4.6"),
        new(SignalKind.Input, "SafetyBarPressed", "192.168.250.1-4.8", 7, ""),
        new(SignalKind.Input, "EstopResetButton", "192.168.250.1-4.9", 10, ""),
        new(SignalKind.Output, "IO-PlatePresentBypass", "192.168.250.1-0.0", 171, ""),
        new(SignalKind.Output, "IO-RackLock", "192.168.250.1-0.1", 1284, ""),
        new(SignalKind.Output, "IO-FireLamp", "192.168.250.1-0.4", 1337, "192.168.250.1-0.7"),
        new(SignalKind.Output, "IO-ResetLamp", "192.168.250.1-0.5", 229, "192.168.250.1-0.8"),
        new(SignalKind.Output, "IO-ClampedFireReleaseLamp", "192.168.250.1-0.6", 2518, "192.168.250.1-0.9"),
        new(SignalKind.Output, "IO-FireLamp", "192.168.250.1-0.7", 1337, "192.168.250.1-0.4"),
        new(SignalKind.Output, "IO-ResetLamp", "192.168.250.1-0.8", 229, "192.168.250.1-0.5"),
        new(SignalKind.Output, "IO-ClampedFireReleaseLamp", "192.168.250.1-0.9", 2518, "192.168.250.1-0.6"),
        new(SignalKind.Output, "IO-EstopResetLamp", "192.168.250.1-0.12", 2, ""),
        new(SignalKind.Output, "IO-StudPinUp", "192.168.250.1-0.13", 654, "192.168.250.1-1.8"),
        new(SignalKind.Output, "IO-StudPinUp2", "192.168.250.1-0.14", 160, "192.168.250.1-1.9"),
        new(SignalKind.Output, "IO-PlateClampLock", "192.168.250.1-1.0", 907, "192.168.250.1-1.11"),
        new(SignalKind.Output, "IO-PlateClampLift10mm", "192.168.250.1-1.1", 915, "192.168.250.1-1.12"),
        new(SignalKind.Output, "IO-PlateSupport", "192.168.250.1-1.2", 159, "192.168.250.1-1.13"),
        new(SignalKind.Output, "IO-SideClamp", "192.168.250.1-1.5", 852, ""),
        new(SignalKind.Output, "IO-StudPinUp", "192.168.250.1-1.8", 630, "192.168.250.1-0.13"),
        new(SignalKind.Output, "IO-StudPinUp2", "192.168.250.1-1.9", 162, "192.168.250.1-0.14"),
        new(SignalKind.Output, "IO-PlateClampLock", "192.168.250.1-1.11", 901, "192.168.250.1-1.0"),
        new(SignalKind.Output, "IO-PlateClampLift10mm", "192.168.250.1-1.12", 905, "192.168.250.1-1.1"),
        new(SignalKind.Output, "IO-PlateSupport", "192.168.250.1-1.13", 159, "192.168.250.1-1.2"),
        new(SignalKind.Output, "IO-LowerGunFire", "192.168.250.1-4.0", 1152, "192.168.250.1-4.2"),
        new(SignalKind.Output, "IO-UpperGunFire", "192.168.250.1-4.1", 1152, "192.168.250.1-4.3"),
        new(SignalKind.Output, "IO-LowerGunFire", "192.168.250.1-4.2", 1142, "192.168.250.1-4.0"),
        new(SignalKind.Output, "IO-UpperGunFire", "192.168.250.1-4.3", 1142, "192.168.250.1-4.1"),
        new(SignalKind.Output, "IO-PlateClamp", "192.168.250.1-4.4", 914, "192.168.250.1-4.5"),
        new(SignalKind.Output, "IO-PlateClamp", "192.168.250.1-4.5", 904, "192.168.250.1-4.4"),
        new(SignalKind.Output, "IO-PlateClampUp", "192.168.250.1-4.6", 160, "192.168.250.1-4.7"),
        new(SignalKind.Output, "IO-PlateClampUp", "192.168.250.1-4.7", 156, "192.168.250.1-4.6"),
        new(SignalKind.Output, "IO-UpperGripper", "192.168.250.1-4.8", 163, "192.168.250.1-4.9"),
        new(SignalKind.Output, "IO-UpperGripper", "192.168.250.1-4.9", 163, "192.168.250.1-4.8"),
        new(SignalKind.Output, "IO-LowerGripper", "192.168.250.1-4.10", 167, "192.168.250.1-4.11"),
        new(SignalKind.Output, "IO-LowerGripper", "192.168.250.1-4.11", 167, "192.168.250.1-4.10"),
        new(SignalKind.Output, "IO-HorizStudClamp", "192.168.250.1-4.12", 780, "192.168.250.1-4.13"),
        new(SignalKind.Output, "IO-HorizStudClamp", "192.168.250.1-4.13", 804, "192.168.250.1-4.12"),
        new(SignalKind.Output, "IO-TopStudClamp", "192.168.250.1-4.14", 922, "192.168.250.1-4.15"),
        new(SignalKind.Output, "IO-TopStudClamp", "192.168.250.1-4.15", 914, "192.168.250.1-4.14"),
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
