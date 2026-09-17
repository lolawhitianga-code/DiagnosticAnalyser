namespace DiagFileMonitor.Core.Fleet;

/// <summary>Why a machine is on the list, and who should act on it.</summary>
public enum SignalKindAI
{
    /// <summary>Sales: there is a machine or an upgrade to sell, and the evidence for it.</summary>
    Sales,

    /// <summary>Support: something needs doing before the customer rings us.</summary>
    Support,

    /// <summary>Both: the same evidence is an argument either way round.</summary>
    Either
}

/// <summary>One reason to pick up the phone.</summary>
public record Signal(
    SignalKindAI For,
    string Headline,
    string Evidence,
    string Ask,
    int Weight)
{
    /// <summary>What to say, already joined up, because a lead nobody can act on is not a lead.</summary>
    public string Describe() => $"{Headline}. {Evidence} {Ask}";
}

public class RadarEntry
{
    public FleetSnapshot Machine { get; init; } = new();
    public IReadOnlyList<Signal> Signals { get; init; } = Array.Empty<Signal>();

    public int Score => Signals.Sum(s => s.Weight);

    public bool Any => Signals.Count > 0;

    public string Who => Signals.Select(s => s.For).Distinct().Count() > 1
        ? "sales and support"
        : Signals[0].For switch
        {
            SignalKindAI.Sales => "sales",
            SignalKindAI.Support => "support",
            _ => "sales and support"
        };
}

/// <summary>
/// The list of customers worth ringing this week, and the reason for each.
/// <para>
/// A manufacturer's two hardest questions are "who is about to have a problem" and "who is ready
/// to buy something". Both are answerable from data that is already on the disk, and neither is
/// answerable by looking at one bundle at a time.
/// </para>
/// <para>
/// Every signal carries the evidence that raised it, in the customer's own numbers. A lead that
/// says "call them" is noise; one that says "their machine has done 46 boards an hour and is
/// averaging 32, and they have sent us four bundles this month" is a conversation.
/// </para>
/// <para>
/// Deliberately conservative. A radar that lights up for everything gets switched off, and the
/// thresholds here are set so a quiet fleet produces a short list.
/// </para>
/// </summary>
public static class OpportunityRadar
{
    /// <summary>Under this share of its own best, a machine is leaving real output on the floor.</summary>
    private const double HeadroomThreshold = 0.70;

    /// <summary>Bundles in ninety days that says somebody is struggling rather than unlucky.</summary>
    private const int BusySupport = 3;

    /// <summary>Longer than this without a bundle, on a machine we used to hear from, is a quiet machine.</summary>
    private const int GoneQuietDays = 180;

    public static IReadOnlyList<RadarEntry> Scan(
        IReadOnlyList<FleetSnapshot> fleet, DateTime asAtUtc)
    {
        return fleet
            .Select(machine => new RadarEntry { Machine = machine, Signals = SignalsFor(machine, asAtUtc) })
            .Where(entry => entry.Any)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Machine.Customer)
            .ToList();
    }

    private static List<Signal> SignalsFor(FleetSnapshot machine, DateTime asAtUtc)
    {
        var signals = new List<Signal>();

        // ---- the machine is working hard and still not keeping up with itself
        if (machine.Output.ShareOfItsOwnBest is { } share && share < HeadroomThreshold
            && machine.Output.DaysWithOutput >= 5)
        {
            var lost = machine.Output.BestRatePerHour - machine.Output.AverageRatePerHour;

            signals.Add(new Signal(
                SignalKindAI.Either,
                "Running well under what it has already proved it can do",
                $"Best day {machine.Output.BestRatePerHour:F0} {machine.Output.Unit} an hour, "
                + $"averaging {machine.Output.AverageRatePerHour:F0} - a gap of {lost:F0} an hour "
                + $"across {machine.Output.DaysWithOutput} producing day(s).",
                "Worth a service visit before it is worth a quote: find out whether it is the "
                + "machine, the timber or the run sizes.",
                Weight: 30));
        }

        // ---- somebody is fighting the machine
        if (machine.RecentBundles >= BusySupport)
        {
            signals.Add(new Signal(
                SignalKindAI.Support,
                "Sending us a lot of bundles",
                $"{machine.RecentBundles} in the last ninety days"
                + (machine.RepeatSubmissions > 0
                    ? $", {machine.RepeatSubmissions} of them within hours of the one before."
                    : "."),
                "Repeat bundles usually mean the last answer did not land. Ring them rather than "
                + "waiting for the next one.",
                Weight: 40));
        }

        // ---- the machine has gone quiet
        if (machine.Bundles >= 2 && machine.DaysSinceLastBundle is > GoneQuietDays and < int.MaxValue)
        {
            signals.Add(new Signal(
                SignalKindAI.Sales,
                "Gone quiet",
                $"Nothing from this machine for {machine.DaysSinceLastBundle} days, after "
                + $"{machine.Bundles} bundle(s) before that.",
                "Either it is running beautifully or it is not being used. Both are worth knowing, "
                + "and only one of them is good news.",
                Weight: 15));
        }

        // ---- the timber says the machine may be the wrong size for the work
        if (machine.Timber.Any && machine.Timber.DeepestMember >= 240)
        {
            signals.Add(new Signal(
                SignalKindAI.Sales,
                "Stocking deep material",
                $"Runs up to {machine.Timber.DeepestMember} mm ({string.Join(", ", machine.Timber.Sizes)}).",
                "A site carrying deep members is doing heavy floor or rafter work. Check the "
                + "machine on the floor is rated for what they are now buying timber for.",
                Weight: 10));
        }

        // ---- old software, which is the cheapest fix a manufacturer ever sells
        if (machine.SoftwareVersion is { Length: > 0 } version && LooksOld(version))
        {
            signals.Add(new Signal(
                SignalKindAI.Support,
                "Running old software",
                $"Reported version {version}.",
                "An upgrade is the cheapest thing we can do for them and it closes out bugs we "
                + "have already fixed for somebody else.",
                Weight: 20));
        }

        return signals;
    }

    /// <summary>
    /// Anything before 2.5 on the versions seen so far.
    /// <para>
    /// Crude on purpose, and it is the one rule here that will go stale. When the release list is
    /// somewhere the app can read, this should read it instead of guessing from a number.
    /// </para>
    /// </summary>
    private static bool LooksOld(string version)
    {
        var first = version.TrimStart('V', 'v').Split('.');

        return first.Length >= 2
               && int.TryParse(first[0], out var major)
               && int.TryParse(first[1], out var minor)
               && (major < 2 || (major == 2 && minor < 5));
    }
}
