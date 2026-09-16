namespace DiagFileMonitor.Core.Production;

/// <summary>A recurring quiet period. <see cref="FoundInDataOnly"/> marks one that is not on the
/// site's official break sheet but shows up consistently in the timestamps.</summary>
public record ShiftBreak(string Name, TimeOnly From, TimeOnly To, bool FoundInDataOnly = false)
{
    public double Minutes => (To.ToTimeSpan() - From.ToTimeSpan()).TotalMinutes;

    public bool Contains(TimeOnly time) => time >= From && time < To;
}

/// <summary>
/// How a site works, used to turn panel timestamps into availability.
/// <para>
/// Availability is deliberately NOT derived from MachineStarted / MachineStopped. Those events go
/// missing - the controller simply does not log a stop over a weekend - which turns an ordinary
/// gap into a multi-day "powered" span. A shift model applied to completed-panel timestamps is
/// steadier.
/// </para>
/// <para>
/// A model is an assumption, not a measurement. Any report built on one says so and prints the
/// model it used, because two sites on different models are not comparable.
/// </para>
/// </summary>
public class ShiftModel
{
    public string Name { get; init; } = "Unnamed";

    public TimeOnly ShiftStart { get; init; } = new(0, 0);
    public TimeOnly ShiftEnd { get; init; } = new(23, 59);

    public IReadOnlyList<ShiftBreak> Breaks { get; init; } = Array.Empty<ShiftBreak>();

    /// <summary>
    /// No roster is known, so availability is not worked out at all.
    /// <para>
    /// Availability rests entirely on a roster. Inventing one produces a number that looks
    /// measured and is not, and two sites on different invented rosters are not comparable. Where
    /// nobody has confirmed the shift, saying so is the honest answer - the output figures are
    /// still measured and still worth having.
    /// </para>
    /// </summary>
    public bool Ignored { get; init; }

    /// <summary>
    /// A gap between completed panels longer than this, outside a scheduled break, counts as an
    /// unplanned stop. Twenty minutes is what the reference implementation uses.
    /// </summary>
    public double UnplannedStopMinutes { get; init; } = 20;

    /// <summary>
    /// The ceiling on a single gap. Past this the log has simply stopped for a while - a weekend,
    /// a shutdown - and the time is unknown rather than lost production.
    /// </summary>
    public double MaxGapMinutes { get; init; } = 16 * 60;

    public double PlannedMinutesPerDay => Ignored
        ? 0
        : (ShiftEnd.ToTimeSpan() - ShiftStart.ToTimeSpan()).TotalMinutes - Breaks.Sum(b => b.Minutes);

    public bool InBreak(DateTime moment) =>
        Breaks.Any(b => b.Contains(TimeOnly.FromDateTime(moment)));

    /// <summary>
    /// Minutes between two moments on the same day that were never rostered production: scheduled
    /// breaks, and anything before the shift started or after it ended.
    /// <para>
    /// This is what makes a gap honest. Asking only whether a gap <i>began</i> during a break
    /// throws away a three hour stoppage that happened to start at lunch; asking only the
    /// wall-clock length counts lunch itself as a stoppage. Taking the break and off-shift minutes
    /// out of the middle of the gap is the only answer that survives both.
    /// </para>
    /// </summary>
    public double NonProductiveMinutes(DateTime from, DateTime to)
    {
        if (to <= from) return 0;

        var a = from.TimeOfDay.TotalMinutes;
        var b = to.TimeOfDay.TotalMinutes;

        var start = ShiftStart.ToTimeSpan().TotalMinutes;
        var end = ShiftEnd.ToTimeSpan().TotalMinutes;

        // Before the shift began, and after it ended.
        var outside = Math.Max(0, Math.Min(b, start) - a) + Math.Max(0, b - Math.Max(a, end));

        // Each break, clipped to the shift and then to the interval asked about.
        var inBreaks = Breaks.Sum(window =>
        {
            var breakStart = Math.Max(window.From.ToTimeSpan().TotalMinutes, start);
            var breakEnd = Math.Min(window.To.ToTimeSpan().TotalMinutes, end);

            return Math.Max(0, Math.Min(b, breakEnd) - Math.Max(a, breakStart));
        });

        return outside + inBreaks;
    }

    /// <summary>The minutes between two moments that were rostered production.</summary>
    public double ProductiveMinutes(DateTime from, DateTime to) =>
        Math.Max(0, (to - from).TotalMinutes - NonProductiveMinutes(from, to));

    /// <summary>
    /// Carters Auckland: close to round the clock, so the shift is the whole day and the recurring
    /// quiet periods are listed as breaks.
    /// <para>
    /// These windows were read off the delivered Line 3 and Line 5 reports, which found them by
    /// histogramming panel completions rather than from a site break sheet. The 03:30 overnight gap
    /// is explicitly flagged in those reports as not being on either machine's official sheet.
    /// </para>
    /// </summary>
    public static ShiftModel CartersAucklandRoundTheClock { get; } = new()
    {
        Name = "Carters Auckland - round the clock, six breaks",
        ShiftStart = new TimeOnly(0, 0),
        ShiftEnd = new TimeOnly(23, 59),
        Breaks = new[]
        {
            new ShiftBreak("Overnight gap", new TimeOnly(3, 30), new TimeOnly(4, 0), FoundInDataOnly: true),
            new ShiftBreak("Morning smoko", new TimeOnly(7, 0), new TimeOnly(7, 30)),
            new ShiftBreak("Mid-morning", new TimeOnly(10, 30), new TimeOnly(11, 0)),
            new ShiftBreak("Lunch", new TimeOnly(12, 30), new TimeOnly(13, 0)),
            new ShiftBreak("Afternoon smoko", new TimeOnly(18, 30), new TimeOnly(19, 0)),
            new ShiftBreak("Evening smoko", new TimeOnly(21, 30), new TimeOnly(22, 0))
        }
    };

    /// <summary>
    /// A single day shift, for a site that runs one. Kept as the default for a site nobody has
    /// modelled yet, because claiming a 24 hour day for a machine that runs eight would make its
    /// availability look far worse than it is.
    /// </summary>
    /// <summary>
    /// Reads breaks written the way a person would: <c>07:00-07:30, 12:30-13:00</c>. Anything that
    /// is not a pair of times is skipped rather than rejecting the whole line - somebody typing a
    /// roster should not lose the rest of it to one typo.
    /// </summary>
    public static IReadOnlyList<ShiftBreak> ParseBreaks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<ShiftBreak>();

        var windows = new List<ShiftBreak>();

        foreach (var piece in text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var ends = piece.Split(new[] { '-', '\u2013' }, 2);
            if (ends.Length != 2) continue;

            if (!TimeOnly.TryParse(ends[0].Trim(), out var from)) continue;
            if (!TimeOnly.TryParse(ends[1].Trim(), out var to)) continue;
            if (to <= from) continue;

            windows.Add(new ShiftBreak($"Break {windows.Count + 1}", from, to));
        }

        return windows;
    }

    /// <summary>The breaks written back out, for the box they were typed into.</summary>
    public string DescribeBreaks() =>
        string.Join(", ", Breaks.Select(b => $"{b.From:HH\\:mm}-{b.To:HH\\:mm}"));

    /// <summary>
    /// No roster. Output is still measured; availability is simply not reported.
    /// </summary>
    public static ShiftModel NoShift { get; } = new()
    {
        Name = "No shift model - availability not reported",
        Ignored = true
    };

    public static ShiftModel SingleDayShift { get; } = new()
    {
        Name = "Single day shift 07:00-17:00, two smokos and lunch",
        ShiftStart = new TimeOnly(7, 0),
        ShiftEnd = new TimeOnly(17, 0),
        Breaks = new[]
        {
            new ShiftBreak("Morning smoko", new TimeOnly(10, 0), new TimeOnly(10, 15)),
            new ShiftBreak("Lunch", new TimeOnly(12, 30), new TimeOnly(13, 0)),
            new ShiftBreak("Afternoon smoko", new TimeOnly(14, 30), new TimeOnly(14, 45))
        }
    };
}
