namespace DiagFileMonitor.Core.Production;

/// <summary>
/// Turns classified panels into day, month and whole-window figures.
/// <para>
/// Every calendar day between the first and last panel gets a row, including days with nothing on
/// them. A day where a machine sat at zero while the site was working is the most useful thing
/// this data surfaces, and silently skipping empty days would hide exactly that.
/// </para>
/// </summary>
public static class ProductionAnalyser
{
    public static ProductionSummary Summarise(
        IReadOnlyList<PanelRecord> panels, ShiftModel shift, string serialNumber = "", string site = "")
    {
        if (panels.Count == 0)
        {
            return new ProductionSummary { SerialNumber = serialNumber, Site = site, Shift = shift };
        }

        var completed = panels.Where(p => p.Outcome == PanelOutcome.Completed).ToList();
        var from = DateOnly.FromDateTime(panels.Min(p => p.EndedAt));
        var to = DateOnly.FromDateTime(panels.Max(p => p.EndedAt));

        var byDay = panels.GroupBy(p => p.Day).ToDictionary(g => g.Key, g => g.ToList());
        var days = new List<DayStats>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var onThisDay);
            onThisDay ??= new List<PanelRecord>();

            var done = onThisDay.Where(p => p.Outcome == PanelOutcome.Completed)
                .OrderBy(p => p.EndedAt).ToList();

            var planned = shift.PlannedMinutesPerDay;
            var lost = UnplannedStopMinutes(done, shift);

            days.Add(new DayStats
            {
                Day = day,
                PanelsCompleted = done.Count,
                SteppedPast = onThisDay.Count(p => p.Outcome == PanelOutcome.SteppedPast),
                StoppedByOperator = onThisDay.Count(p => p.Outcome == PanelOutcome.StoppedByOperator),
                Cube = done.Sum(p => p.Cube),
                Lineal = done.Sum(p => p.Lineal),
                // A day with no panels at all is not planned production time. Counting it as
                // planned would drag availability down for a shutdown nobody was rostered for.
                PlannedMinutes = done.Count > 0 ? planned : 0,
                RunMinutes = done.Count > 0 ? Math.Max(0, planned - lost) : 0,
                UnplannedStopMinutes = done.Count > 0 ? lost : 0
            });
        }

        var months = days
            .GroupBy(d => (d.Day.Year, d.Day.Month))
            .Select(g =>
            {
                var planned = g.Sum(d => d.PlannedMinutes);
                return new MonthStats
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    PanelsCompleted = g.Sum(d => d.PanelsCompleted),
                    Cube = g.Sum(d => d.Cube),
                    Lineal = g.Sum(d => d.Lineal),
                    DaysWithOutput = g.Count(d => d.HadOutput),
                    Availability = planned > 0 ? g.Sum(d => d.RunMinutes) / planned : null
                };
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();

        return new ProductionSummary
        {
            SerialNumber = serialNumber,
            Site = site,
            From = from,
            To = to,
            PanelsCompleted = completed.Count,
            SteppedPast = panels.Count(p => p.Outcome == PanelOutcome.SteppedPast),
            StoppedByOperator = panels.Count(p => p.Outcome == PanelOutcome.StoppedByOperator),
            Superseded = panels.Count(p => p.Outcome == PanelOutcome.Superseded),
            Cube = completed.Sum(p => p.Cube),
            Lineal = completed.Sum(p => p.Lineal),
            DaysWithOutput = days.Count(d => d.HadOutput),
            CalendarDays = days.Count,
            Days = days,
            Months = months,
            Shift = shift,
            ImplausibleBuildTimes = completed.Count(p => p.BuildTimeImplausible),
            ZeroBuildTimes = completed.Count(p => p.BuildMinutes == 0)
        };
    }

    /// <summary>
    /// Minutes lost to gaps between completed panels that were not a scheduled break.
    /// <para>
    /// A gap longer than the model's ceiling is a log gap of unknown length - a shutdown, a
    /// weekend - so only the ceiling is counted, not the whole thing.
    /// </para>
    /// </summary>
    private static double UnplannedStopMinutes(IReadOnlyList<PanelRecord> completedInOrder, ShiftModel shift)
    {
        double lost = 0;

        for (var i = 1; i < completedInOrder.Count; i++)
        {
            var previous = completedInOrder[i - 1].EndedAt;
            var next = completedInOrder[i].EndedAt;
            var gap = (next - previous).TotalMinutes;

            if (gap <= shift.UnplannedStopMinutes) continue;

            // A gap that started inside a scheduled break is the break, not a stoppage.
            if (shift.InBreak(previous)) continue;

            lost += Math.Min(gap, shift.MaxGapMinutes);
        }

        return lost;
    }

    /// <summary>
    /// Days where one machine was effectively stopped while another on the same site kept going.
    /// This is the comparison that tells a site something it cannot see from one machine's numbers.
    /// </summary>
    public static IReadOnlyList<DateOnly> LikelyUnplannedDowntime(
        ProductionSummary quiet, ProductionSummary busy,
        double quietShareOfOwnMedian = 0.2, double busyShareOfOwnMedian = 0.3)
    {
        var quietMedian = quiet.MedianPanelsPerDay;
        var busyMedian = busy.MedianPanelsPerDay;
        if (quietMedian <= 0 || busyMedian <= 0) return Array.Empty<DateOnly>();

        var busyByDay = busy.Days.ToDictionary(d => d.Day, d => d.PanelsCompleted);

        return quiet.Days
            .Where(d => d.PanelsCompleted < quietMedian * quietShareOfOwnMedian)
            .Where(d => busyByDay.TryGetValue(d.Day, out var other)
                        && other >= busyMedian * busyShareOfOwnMedian)
            .Select(d => d.Day)
            .ToList();
    }
}
