namespace DiagFileMonitor.Core.Production;

/// <summary>One calendar day's production. Days with no output are kept, not dropped.</summary>
public class DayStats
{
    public DateOnly Day { get; init; }
    public int PanelsCompleted { get; init; }
    public int SteppedPast { get; init; }
    public int StoppedByOperator { get; init; }
    public double Cube { get; init; }
    public double Lineal { get; init; }

    /// <summary>Shift minutes after breaks are taken off.</summary>
    public double PlannedMinutes { get; init; }

    /// <summary>Planned minutes less unplanned stops.</summary>
    public double RunMinutes { get; init; }

    /// <summary>Minutes lost to gaps between panels that were not a scheduled break.</summary>
    public double UnplannedStopMinutes { get; init; }

    public double? Availability => PlannedMinutes > 0 ? RunMinutes / PlannedMinutes : null;

    public bool HadOutput => PanelsCompleted > 0;
}

public class MonthStats
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int PanelsCompleted { get; init; }
    public double Cube { get; init; }
    public double Lineal { get; init; }
    public int DaysWithOutput { get; init; }
    public double? Availability { get; init; }

    public string Label => $"{Year:0000}-{Month:00}";
}

public class ProductionSummary
{
    public string SerialNumber { get; init; } = string.Empty;
    public string Site { get; init; } = string.Empty;

    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public int PanelsCompleted { get; init; }
    public int SteppedPast { get; init; }
    public int StoppedByOperator { get; init; }
    public int Superseded { get; init; }

    public double Cube { get; init; }
    public double Lineal { get; init; }

    public int DaysWithOutput { get; init; }

    /// <summary>Every calendar day in the window, including the ones with nothing on them.</summary>
    public int CalendarDays { get; init; }

    public IReadOnlyList<DayStats> Days { get; init; } = Array.Empty<DayStats>();
    public IReadOnlyList<MonthStats> Months { get; init; } = Array.Empty<MonthStats>();

    public ShiftModel Shift { get; init; } = ShiftModel.SingleDayShift;

    /// <summary>Panels whose logged build time was past the plausible ceiling.</summary>
    public int ImplausibleBuildTimes { get; init; }

    /// <summary>Completed panels whose build time was logged as exactly zero.</summary>
    public int ZeroBuildTimes { get; init; }

    public double PanelsPerProductionDay => DaysWithOutput > 0 ? PanelsCompleted / (double)DaysWithOutput : 0;

    public double? Availability
    {
        get
        {
            var planned = Days.Sum(d => d.PlannedMinutes);
            return planned > 0 ? Days.Sum(d => d.RunMinutes) / planned : null;
        }
    }

    /// <summary>
    /// Operator-stopped panels as a share of everything that closed one way or another.
    /// Superseded panels are deliberately excluded - see <see cref="PanelOutcome.Superseded"/>.
    /// </summary>
    public double? FaultRate
    {
        get
        {
            var closed = PanelsCompleted + SteppedPast + StoppedByOperator;
            return closed > 0 ? StoppedByOperator / (double)closed : null;
        }
    }

    /// <summary>Median panels on a day that produced anything, used to find a dead day.</summary>
    public double MedianPanelsPerDay
    {
        get
        {
            var counts = Days.Where(d => d.HadOutput).Select(d => d.PanelsCompleted).OrderBy(c => c).ToList();
            if (counts.Count == 0) return 0;
            return counts.Count % 2 == 1
                ? counts[counts.Count / 2]
                : (counts[counts.Count / 2 - 1] + counts[counts.Count / 2]) / 2.0;
        }
    }
}
