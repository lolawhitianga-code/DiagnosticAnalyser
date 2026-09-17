namespace DiagFileMonitor.Core.Fleet;

/// <summary>How sure we are of a number, because a manufacturer quotes these to customers.</summary>
public enum Measured
{
    /// <summary>Counted from the machine's own logs.</summary>
    FromTheLogs,

    /// <summary>Worked out from what was counted, on a stated assumption.</summary>
    Derived,

    /// <summary>Nothing in the data says this. Shown as unknown rather than as a zero.</summary>
    NotKnown
}

/// <summary>What one machine has produced, in whichever unit its own kind of machine counts in.</summary>
public record OutputRecord(
    string Unit,
    double Total,
    double CubicMetres,
    double LinealMetres,
    int DaysWithOutput,
    double AverageRatePerHour,
    double BestRatePerHour,
    DateOnly? From,
    DateOnly? To)
{
    public static readonly OutputRecord Nothing =
        new("nothing measured", 0, 0, 0, 0, 0, 0, null, null);

    public bool Any => DaysWithOutput > 0;

    /// <summary>
    /// How much of its own demonstrated best the machine is actually getting, across the window.
    /// <para>
    /// Measured against the machine's own best day on its own site, not against a brochure figure
    /// or another customer. That makes it an argument a customer cannot wave away: their machine
    /// has already done this, on their timber, with their people.
    /// </para>
    /// </summary>
    public double? ShareOfItsOwnBest => BestRatePerHour > 0 && AverageRatePerHour > 0
        ? AverageRatePerHour / BestRatePerHour
        : null;
}

/// <summary>The timber a site actually runs, read from its stock list.</summary>
public record TimberProfile(IReadOnlyList<string> Sizes, IReadOnlyList<int> Lengths)
{
    public static readonly TimberProfile Unknown = new(Array.Empty<string>(), Array.Empty<int>());

    public bool Any => Sizes.Count > 0;

    /// <summary>The deepest member the site stocks, in millimetres. Drives machine capacity.</summary>
    public int DeepestMember => Sizes
        .Select(s => s.Split('x').LastOrDefault())
        .Select(w => int.TryParse(w, out var value) ? value : 0)
        .DefaultIfEmpty(0)
        .Max();
}

/// <summary>
/// Everything known about one machine, gathered in one place.
/// <para>
/// The support side of this product answers "what is wrong with this machine". A manufacturer also
/// has to answer "how is the installed base", "who should we call", and "what does this model
/// really do" - and those questions need one row per machine rather than one report per bundle.
/// </para>
/// </summary>
public class FleetSnapshot
{
    public string SerialNumber { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public string Site { get; init; } = string.Empty;
    public string SoftwareVersion { get; init; } = string.Empty;

    public DateTime? FirstSeenUtc { get; init; }
    public DateTime? LastSeenUtc { get; init; }

    /// <summary>Support bundles this machine has sent us, ever.</summary>
    public int Bundles { get; init; }

    /// <summary>Bundles in the last ninety days - the support load that is actually current.</summary>
    public int RecentBundles { get; init; }

    /// <summary>
    /// Bundles sent within a few hours of another, which is somebody sending the same problem
    /// twice because the first one did not get them an answer.
    /// </summary>
    public int RepeatSubmissions { get; init; }

    public OutputRecord Output { get; init; } = OutputRecord.Nothing;
    public TimberProfile Timber { get; init; } = TimberProfile.Unknown;

    /// <summary>Distinct machine faults seen across this machine's bundles, most frequent first.</summary>
    public IReadOnlyList<(string Fault, int Times)> Faults { get; init; } =
        Array.Empty<(string, int)>();

    /// <summary>Settings changed on this machine, and by whom.</summary>
    public IReadOnlyList<(DateTime When, string What, string By)> Changes { get; init; } =
        Array.Empty<(DateTime, string, string)>();

    public int DaysSinceLastBundle => LastSeenUtc is { } last
        ? (int)(DateTime.UtcNow - last).TotalDays
        : int.MaxValue;

    /// <summary>
    /// How long we have been watching this machine. Everything trend-shaped needs this, and a
    /// machine we have only seen once cannot have a trend read off it.
    /// </summary>
    public int DaysKnown => FirstSeenUtc is { } first && LastSeenUtc is { } last
        ? Math.Max(1, (int)(last - first).TotalDays)
        : 0;

    public bool EnoughHistoryForATrend => DaysKnown >= 21 && Bundles >= 2;
}
