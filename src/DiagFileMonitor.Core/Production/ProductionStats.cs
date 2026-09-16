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

    /// <summary>Rostered minutes before the first panel of the day.</summary>
    public double StartupMinutes { get; init; }

    /// <summary>Rostered minutes after the last panel of the day.</summary>
    public double TailMinutes { get; init; }

    /// <summary>How many gaps counted as unplanned stops.</summary>
    public int UnplannedStops { get; init; }

    public int Faults { get; init; }

    public double? Availability => PlannedMinutes > 0 ? RunMinutes / PlannedMinutes : null;

    /// <summary>Panels an hour while the machine was actually running.</summary>
    public double? RateWhileRunning => RunMinutes > 0 ? PanelsCompleted / (RunMinutes / 60) : null;

    /// <summary>Panels an hour measured across the whole rostered shift.</summary>
    public double? RateAcrossShift => PlannedMinutes > 0 ? PanelsCompleted / (PlannedMinutes / 60) : null;

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
    public int RanButNailedNothing { get; init; }
    public int AbandonedPartWay { get; init; }
    public int Superseded { get; init; }

    /// <summary>Panels the machine was asked for and did not make. Stepped past is not one.</summary>
    public int Faults => StoppedByOperator + RanButNailedNothing + AbandonedPartWay;

    /// <summary>Days the fastener counter was not reporting, so a zero count said nothing.</summary>
    public int DaysFastenerCounterOff { get; init; }

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
            if (Shift.Ignored) return null;

            var planned = Days.Sum(d => d.PlannedMinutes);
            return planned > 0 ? Days.Sum(d => d.RunMinutes) / planned : null;
        }
    }

    public double PlannedMinutes => Days.Sum(d => d.PlannedMinutes);
    public double RunMinutes => Days.Sum(d => d.RunMinutes);
    public double StopMinutes => Days.Sum(d => d.UnplannedStopMinutes);
    public double StartupAndTailMinutes => Days.Sum(d => d.StartupMinutes + d.TailMinutes);
    public int UnplannedStops => Days.Sum(d => d.UnplannedStops);

    /// <summary>Panels an hour while running, and across the whole rostered shift.</summary>
    public double? RateWhileRunning => RunMinutes > 0 ? PanelsCompleted / (RunMinutes / 60) : null;
    public double? RateAcrossShift => PlannedMinutes > 0 ? PanelsCompleted / (PlannedMinutes / 60) : null;

    /// <summary>
    /// Panels that went wrong, as a share of everything that closed one way or another. Stepped
    /// past is excluded because it is routine, and superseded because an unclosed start is the
    /// operator moving around the HMI - see <see cref="PanelOutcome.Superseded"/>.
    /// </summary>
    public double? FaultRate
    {
        get
        {
            var closed = PanelsCompleted + SteppedPast + Faults;
            return closed > 0 ? Faults / (double)closed : null;
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
