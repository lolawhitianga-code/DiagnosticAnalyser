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
    /// A gap between completed panels longer than this, outside a scheduled break, counts as an
    /// unplanned stop. Twenty minutes is what the reference implementation uses.
    /// </summary>
    public double UnplannedStopMinutes { get; init; } = 20;

    /// <summary>
    /// The ceiling on a single gap. Past this the log has simply stopped for a while - a weekend,
    /// a shutdown - and the time is unknown rather than lost production.
    /// </summary>
    public double MaxGapMinutes { get; init; } = 16 * 60;

    public double PlannedMinutesPerDay =>
        (ShiftEnd.ToTimeSpan() - ShiftStart.ToTimeSpan()).TotalMinutes - Breaks.Sum(b => b.Minutes);

    public bool InBreak(DateTime moment) =>
        Breaks.Any(b => b.Contains(TimeOnly.FromDateTime(moment)));

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
