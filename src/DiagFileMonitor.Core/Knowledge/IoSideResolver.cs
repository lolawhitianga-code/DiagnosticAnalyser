using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One address, named and sided - or said plainly to be unresolved.</summary>
public record ResolvedSignal(
    SignalId Id,
    MachineSide Side,
    string MachineName,
    double LoggedOnSeconds,
    double CounterOnSeconds,
    double SeparationSeconds,
    bool FromNameAlone = false)
{
    public bool IsResolved => Side != MachineSide.Unknown;

    /// <summary>How far the matched duty figure sat from the logged one.</summary>
    public double ResidualSeconds => Math.Abs(CounterOnSeconds - LoggedOnSeconds);

    /// <summary>Two sides that ran within this of each other cannot be told apart at all.</summary>
    internal const double MinimumSeparation = 0.05;

    /// <summary>A match further off than this is not a fingerprint match, whatever else fits.</summary>
    internal const double MaximumResidual = 0.25;
}

public record SideFindings(
    IReadOnlyList<ResolvedSignal> Resolved,
    IReadOnlyList<SignalId> Ambiguous,
    TimeSpan CounterWindowStart,
    TimeSpan CounterWindowEnd,
    double FitErrorSeconds)
{
    public bool Any => Resolved.Count > 0;

    /// <summary>
    /// The ones safe to quote. Anything worked out from a duration needs the counter window to
    /// be this log's hour; anything read straight off the machine's own name does not.
    /// </summary>
    public IEnumerable<ResolvedSignal> Trustworthy =>
        Resolved.Where(r => r.FromNameAlone || WindowTrusted);

    /// <summary>
    /// Judged on the typical signal, not the total. A handful of outputs are energised across
    /// the hour boundary and the counter carries the earlier part of that stretch into the new
    /// hour, so those few are tens of seconds out however well the window is chosen. The median
    /// ignores them; on a window that is genuinely this log's hour it comes out near zero.
    /// </summary>
    public bool WindowTrusted => FitErrorSeconds < 0.25;
}

/// <summary>
/// Works out which physical side of the machine an output address belongs to, by matching how
/// long the log says it was energised against how long CloudLog/maint_data.json says
/// "FixedSide/PlateClamp" was energised.
/// <para>
/// MachineLog.txt gives addresses with no side; maint_data.json gives sides with no address.
/// Neither is any use for this on its own. The join between them is the on-time, which runs to
/// seven decimal places and so is effectively a fingerprint - matches come out within a few
/// hundredths of a second.
/// </para>
/// <para>
/// It only works for a pair whose two halves ran for measurably different lengths of time in the
/// counted hour. Where both sides clamped and released together all hour their on-times are
/// identical, there is nothing to tell them apart, and this reports them as ambiguous rather
/// than picking one. That is the whole point: guessing a side from a neighbouring pair is what
/// got the M21737 partner address wrong.
/// </para>
/// </summary>
public static class IoSideResolver
{
    /// <summary>The counters cover the current clock hour, so that is where the search starts.</summary>
    public static SideFindings Resolve(IoTimeline timeline, IReadOnlyList<OutputDuty> duties)
    {
        if (timeline.Changes.Count == 0 || duties.Count == 0)
            return new SideFindings(Array.Empty<ResolvedSignal>(), Array.Empty<SignalId>(),
                TimeSpan.Zero, TimeSpan.Zero, double.MaxValue);

        var end = timeline.LastTime;
        var outputs = timeline.Signals.Where(s => s.Kind == SignalKind.Output).ToList();

        // Every whole hour the log covers is a candidate; take the one whose on-times fit best.
        var candidates = Enumerable
            .Range(0, (int)end.TotalHours + 1)
            .Select(h => TimeSpan.FromHours(h))
            .Where(t => t <= end)
            .ToList();

        var best = candidates
            .Select(start => (Start: start, Error: FitError(timeline, outputs, duties, start, end)))
            .OrderBy(x => x.Error)
            .FirstOrDefault();

        var resolved = new List<ResolvedSignal>();
        var ambiguous = new List<SignalId>();

        foreach (var id in outputs)
        {
            var shortName = ShortName(id.Name);
            var choices = duties.Where(d => d.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (choices.Count == 0) continue;

            var logged = OnSeconds(timeline, id, best.Start, end);

            if (choices.Count == 1)
            {
                // The machine has one name for it and that name carries the side, so this holds
                // whether or not the counter window lines up - no timing went into it.
                resolved.Add(new ResolvedSignal(id, choices[0].Side, choices[0].FullName,
                    logged, choices[0].OnSeconds, double.MaxValue, FromNameAlone: true));
                continue;
            }

            var ordered = choices.OrderBy(c => c.OnSeconds).ToList();
            var separation = ordered[^1].OnSeconds - ordered[0].OnSeconds;

            if (separation < ResolvedSignal.MinimumSeparation)
            {
                // The two sides ran for the same length of time. Nothing distinguishes them.
                ambiguous.Add(id);
                continue;
            }

            var nearest = choices.OrderBy(c => Math.Abs(c.OnSeconds - logged)).First();
            var residual = Math.Abs(nearest.OnSeconds - logged);

            // Believe it only when the match lands on the duty figure AND is far closer to it
            // than to the other side's. Either test alone lets a coincidence through.
            if (residual > ResolvedSignal.MaximumResidual || residual > separation / 4)
            {
                ambiguous.Add(id);
                continue;
            }

            resolved.Add(new ResolvedSignal(id, nearest.Side, nearest.FullName,
                logged, nearest.OnSeconds, separation));
        }

        return new SideFindings(resolved, ambiguous, best.Start, end, best.Error);
    }

    /// <summary>MachineLog.txt prefixes output names with "IO-"; maint_data.json does not.</summary>
    private static string ShortName(string name) =>
        name.StartsWith("IO-", StringComparison.OrdinalIgnoreCase) ? name[3..] : name;

    /// <summary>The median gap between a logged on-time and the nearest duty figure.</summary>
    private static double FitError(
        IoTimeline timeline, IReadOnlyList<SignalId> outputs,
        IReadOnlyList<OutputDuty> duties, TimeSpan start, TimeSpan end)
    {
        var gaps = new List<double>();

        foreach (var id in outputs)
        {
            var shortName = ShortName(id.Name);
            var choices = duties.Where(d => d.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (choices.Count == 0) continue;

            var logged = OnSeconds(timeline, id, start, end);
            gaps.Add(choices.Min(c => Math.Abs(c.OnSeconds - logged)));
        }

        if (gaps.Count == 0) return double.MaxValue;

        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    /// <summary>
    /// How long this output was on between start and end. A stretch that began before the window
    /// only counts from the window, and one still on at the end counts up to the end.
    /// </summary>
    private static double OnSeconds(IoTimeline timeline, SignalId id, TimeSpan start, TimeSpan end)
    {
        var total = 0.0;
        TimeSpan? since = null;

        foreach (var change in timeline.HistoryOf(id))
        {
            if (change.On)
            {
                since = change.Time;
            }
            else if (since is { } on)
            {
                total += Overlap(on, change.Time, start, end);
                since = null;
            }
        }

        if (since is { } stillOn) total += Overlap(stillOn, end, start, end);

        return total;
    }

    private static double Overlap(TimeSpan from, TimeSpan to, TimeSpan start, TimeSpan end)
    {
        var a = from > start ? from : start;
        var b = to < end ? to : end;
        return b > a ? (b - a).TotalSeconds : 0.0;
    }
}
