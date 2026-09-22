using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One named point on the model, and what this machine numbers it.</summary>
public record PointHere(
    SignalKind Kind,
    string Name,
    IReadOnlyList<string> PointsHere,
    int InstancesOnModel,
    IReadOnlyList<MachineSide> SidesHere)
{
    /// <summary>The model has more of these than moved in this log.</summary>
    public bool SomeNeverMoved => InstancesOnModel > PointsHere.Count;

    /// <summary>This machine's numbers, each with its side where that was measured.</summary>
    public string Numbers
    {
        get
        {
            if (PointsHere.Count == 0) return "-";

            return string.Join(", ", PointsHere.Select((point, i) =>
            {
                var side = i < SidesHere.Count ? SidesHere[i] : MachineSide.Unknown;
                return side switch
                {
                    MachineSide.FixedSide => $"{point} fixed",
                    MachineSide.FloatingSide => $"{point} floating",
                    MachineSide.Shared => $"{point} shared",
                    _ => point
                };
            }));
        }
    }
}

/// <summary>One address the machine calls by more than one name.</summary>
public record SharedAddress(SignalKind Kind, string Address, IReadOnlyList<string> Names);

public record IoMapFindings(
    IReadOnlyList<PointHere> Known,
    IReadOnlyList<PointHere> NeverMoved,
    IReadOnlyList<SignalId> NotOnTheModel,
    IReadOnlyList<SharedAddress> SharedAddresses,
    int NamesOnModel)
{
    public bool Checked => NamesOnModel > 0;

    public bool Any => Known.Count > 0 || NeverMoved.Count > 0 || NotOnTheModel.Count > 0;
}

/// <summary>
/// Lists what this machine has, by name, with whatever numbers this machine happens to use.
/// <para>
/// This used to compare addresses and treat a difference as a warning. That was the wrong way
/// round. Support's words: <i>"the actual number of the IO is less important than the name of
/// the IO"</i> - some Wall Extruder DGs run a point on node 5 and some on node 6, and on three
/// Raked Wall Extruder V3s <c>UpperGunUpperIsLow</c> sits at 0.19 on two and 0.18 on the third.
/// None of that is a fault; it is just how that machine is wired.
/// </para>
/// <para>
/// So the name is the identity and the number is read off the log in front of you. What is still
/// worth saying is a name the model has that <b>never moved here</b> - because a log records
/// changes, so a sensor that never came on and a sensor that is not fitted look identical, and
/// that is exactly the gap that cost us the M21737 case.
/// </para>
/// </summary>
public static class IoMapComparison
{
    public static IoMapFindings Check(IoTimeline timeline, string? model)
    {
        var model_ = MachineIoMap.For(model);
        var seen = timeline.Signals;
        var shared = SharedIn(seen);

        if (model_.Count == 0)
            return new IoMapFindings(
                Array.Empty<PointHere>(), Array.Empty<PointHere>(), Array.Empty<SignalId>(), shared, 0);

        var here = seen
            .GroupBy(s => (s.Kind, Name: MachineIoMap.Flatten(s.Name)))
            .ToDictionary(g => g.Key, g => g.Select(s => ControlPlatformCheck.Point(s.Address))
                                            .Distinct(StringComparer.OrdinalIgnoreCase)
                                            .OrderBy(Order)
                                            .ToList());

        var known = new List<PointHere>();
        var missing = new List<PointHere>();

        foreach (var point in model_)
        {
            var key = (point.Kind, Name: MachineIoMap.Flatten(point.Name));
            var numbers = here.GetValueOrDefault(key, new List<string>());

            var row = new PointHere(
                point.Kind, point.Name, numbers, point.Instances,
                numbers.Select(point.SideAtPoint).ToList());

            if (numbers.Count == 0) missing.Add(row);
            else
            {
                known.Add(row);
                if (row.SomeNeverMoved) missing.Add(row);
            }
        }

        var onModel = model_
            .Select(p => (p.Kind, Name: MachineIoMap.Flatten(p.Name)))
            .ToHashSet();

        var extra = seen
            .Where(s => !onModel.Contains((s.Kind, MachineIoMap.Flatten(s.Name))))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new IoMapFindings(known, missing, extra, shared, model_.Count);
    }

    /// <summary>Sorts 2.3 before 2.11 rather than after it.</summary>
    private static (int, int) Order(string point)
    {
        var parts = point.Split('.');
        return parts.Length == 2
               && int.TryParse(parts[0], out var module)
               && int.TryParse(parts[1], out var bit)
            ? (module, bit)
            : (int.MaxValue, 0);
    }

    /// <summary>
    /// An address the log calls by two names. On M20771 output 5.0 is IO-Bay2Stops while the
    /// infeed runs and IO-UnloaderUp while the unloader does - one physical output, two labels
    /// depending on what is driving it, never both at once.
    /// </summary>
    private static IReadOnlyList<SharedAddress> SharedIn(IReadOnlyList<SignalId> seen) => seen
        .GroupBy(s => (s.Kind, s.Address))
        .Where(g => g.Select(s => MachineIoMap.Flatten(s.Name)).Distinct().Count() > 1)
        .Select(g => new SharedAddress(
            g.Key.Kind,
            g.Key.Address,
            g.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList()))
        .OrderBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
