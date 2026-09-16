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

            var shape = HowTheDayWentThrough(done, shift);

            days.Add(new DayStats
            {
                Day = day,
                PanelsCompleted = done.Count,
                SteppedPast = onThisDay.Count(p => p.Outcome == PanelOutcome.SteppedPast),
                StoppedByOperator = onThisDay.Count(p => p.Outcome == PanelOutcome.StoppedByOperator),
                Faults = onThisDay.Count(p => p.IsFault),
                Cube = done.Sum(p => p.Cube),
                Lineal = done.Sum(p => p.Lineal),
                PlannedMinutes = shape.Planned,
                RunMinutes = shape.Run,
                UnplannedStopMinutes = shape.Stops,
                UnplannedStops = shape.StopCount,
                StartupMinutes = shape.Startup,
                TailMinutes = shape.Tail
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
                    Availability = planned > 0 && !shift.Ignored
                        ? g.Sum(d => d.RunMinutes) / planned
                        : null
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
            RanButNailedNothing = panels.Count(p => p.Outcome == PanelOutcome.RanButNailedNothing),
            AbandonedPartWay = panels.Count(p => p.Outcome == PanelOutcome.AbandonedPartWay),
            DaysFastenerCounterOff = panels
                .Where(p => p.Outcome != PanelOutcome.Superseded)
                .GroupBy(p => p.Day)
                .Count(g => g.All(p => !p.FastenerCounterLive)),
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

    private record ShiftShape(double Planned, double Run, double Stops, int StopCount,
        double Startup, double Tail);

    /// <summary>
    /// How one day's rostered time was spent.
    /// <para>
    /// Rostered time less the wait before the first panel, the wait after the last, and every gap
    /// in between that was longer than the threshold. Each of those has its break and off-shift
    /// minutes taken out of the middle, so a gap that happens to span lunch has lunch deducted
    /// rather than the whole gap being counted or the whole gap being dismissed.
    /// </para>
    /// </summary>
    private static ShiftShape HowTheDayWentThrough(IReadOnlyList<PanelRecord> completedInOrder, ShiftModel shift)
    {
        if (shift.Ignored || completedInOrder.Count == 0)
            return new ShiftShape(0, 0, 0, 0, 0, 0);

        var planned = shift.PlannedMinutesPerDay;
        var day = completedInOrder[0].EndedAt.Date;

        var shiftStart = day + shift.ShiftStart.ToTimeSpan();
        var shiftEnd = day + shift.ShiftEnd.ToTimeSpan();

        var first = completedInOrder[0].EndedAt;
        var last = completedInOrder[^1].EndedAt;

        // Rostered time before anything was made, and after the last thing was.
        var startup = shift.ProductiveMinutes(shiftStart, first < shiftEnd ? first : shiftEnd);
        var tail = shift.ProductiveMinutes(last > shiftStart ? last : shiftStart, shiftEnd);

        double stops = 0;
        var stopCount = 0;

        for (var i = 1; i < completedInOrder.Count; i++)
        {
            var gap = shift.ProductiveMinutes(completedInOrder[i - 1].EndedAt, completedInOrder[i].EndedAt);

            if (gap <= shift.UnplannedStopMinutes) continue;

            stops += Math.Min(gap, shift.MaxGapMinutes);
            stopCount++;
        }

        var run = Math.Max(0, planned - startup - tail - stops);

        return new ShiftShape(planned, run, stops, stopCount, startup, tail);
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
