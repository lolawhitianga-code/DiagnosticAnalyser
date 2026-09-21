using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>A signal name whose address in this log is not the address the map expects.</summary>
public record AddressDisagreement(
    SignalKind Kind,
    string Name,
    IReadOnlyList<string> InThisLog,
    IReadOnlyList<string> InTheMap)
{
    public string Describe() =>
        $"{Name} is at {string.Join(" and ", InThisLog)} here, "
        + $"but the map has {string.Join(" and ", InTheMap)}";
}

/// <summary>One address the machine calls by more than one name.</summary>
public record SharedAddress(SignalKind Kind, string Address, IReadOnlyList<string> Names);

public record IoMapFindings(
    int MapPoints,
    int SeenHere,
    int Confirmed,
    IReadOnlyList<AddressDisagreement> Disagreements,
    IReadOnlyList<SignalId> NotInTheMap,
    IReadOnlyList<SharedAddress> SharedAddresses)
{
    public bool Checked => MapPoints > 0;

    /// <summary>The ones that would send somebody to the wrong terminal.</summary>
    public bool AnythingWorrying => Disagreements.Count > 0;
}

/// <summary>
/// Holds one machine's I/O against the map for its model, and reports where they differ.
/// <para>
/// Three machines of the same model on the same control platform share 74 addresses and agree on
/// the name of every one - but not all of them. On M20771 <c>UpperGunUpperIsLow</c> sits at
/// <c>0.18</c> where M21737 and M21844 both have <c>0.19</c>. One bit, one machine, and a map
/// quoted as fact would have sent a technician to the wrong terminal.
/// </para>
/// <para>
/// So the map is checked against every log rather than trusted over it. A name at a different
/// address is the finding that matters; points missing from a short log are ordinary, and points
/// the map has never seen are usually an option fitted to that machine and not to the others -
/// M20771 carries a whole infeed, lifter and unloader subsystem on modules 3 and 5 that neither
/// of the other two has.
/// </para>
/// </summary>
public static class IoMapComparison
{
    public static IoMapFindings Check(IoTimeline timeline, string? model, ControlPlatform platform)
    {
        var map = MachineIoMap.For(model, platform);
        var seen = timeline.Signals;

        if (map.Count == 0)
            return new IoMapFindings(0, seen.Count, 0,
                Array.Empty<AddressDisagreement>(), Array.Empty<SignalId>(), SharedIn(seen));

        var mapByName = map
            .GroupBy(p => (p.Kind, p.Name), NameComparer)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Address).OrderBy(a => a).ToList(), NameComparer);

        var mapAddresses = map.Select(p => (p.Kind, p.Address)).ToHashSet();

        var disagreements = new List<AddressDisagreement>();

        foreach (var group in seen.GroupBy(s => (s.Kind, s.Name), NameComparer))
        {
            if (!mapByName.TryGetValue(group.Key, out var expected)) continue;

            var here = group.Select(s => s.Address).OrderBy(a => a).ToList();

            // Only a name sitting somewhere the map does not have it at all is worth raising.
            // A short log showing one half of a pair is ordinary and says nothing is wrong.
            var unexpected = here.Where(a => !expected.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unexpected.Count > 0)
                disagreements.Add(new AddressDisagreement(group.Key.Kind, group.Key.Name, unexpected, expected));
        }

        var confirmed = seen.Count(s => mapAddresses.Contains((s.Kind, s.Address)));

        var unknown = seen
            .Where(s => !mapAddresses.Contains((s.Kind, s.Address)))
            .OrderBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new IoMapFindings(map.Count, seen.Count, confirmed, disagreements, unknown, SharedIn(seen));
    }

    /// <summary>
    /// An address the log calls by two names. On M20771 output 5.0 is IO-Bay2Stops while the
    /// infeed runs and IO-UnloaderUp while the unloader does - one physical output, two labels
    /// depending on what is driving it, never both at once. Worth saying, because "what is 5.0"
    /// then has two right answers.
    /// </summary>
    private static IReadOnlyList<SharedAddress> SharedIn(IReadOnlyList<SignalId> seen) => seen
        .GroupBy(s => (s.Kind, s.Address))
        .Where(g => g.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        .Select(g => new SharedAddress(
            g.Key.Kind,
            g.Key.Address,
            g.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList()))
        .OrderBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static readonly NameKeyComparer NameComparer = new();

    /// <summary>Names differ in spacing and case between machines - "E Stop" and "Estop".</summary>
    private sealed class NameKeyComparer : IEqualityComparer<(SignalKind Kind, string Name)>
    {
        public bool Equals((SignalKind Kind, string Name) a, (SignalKind Kind, string Name) b) =>
            a.Kind == b.Kind && Flatten(a.Name) == Flatten(b.Name);

        public int GetHashCode((SignalKind Kind, string Name) key) =>
            HashCode.Combine(key.Kind, Flatten(key.Name));

        private static string Flatten(string name) =>
            new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
