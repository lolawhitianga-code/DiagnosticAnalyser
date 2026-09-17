using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// One time the machine was driven into position and no cut followed.
/// </summary>
public class SilentWait
{
    public TimeSpan RequestedAt { get; init; }

    /// <summary>What was commanded, as the log writes it: "Trolley 339.1", "SawRotation 90".</summary>
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();

    /// <summary>When the axis reported it had arrived, where the log says so.</summary>
    public TimeSpan? InPositionAt { get; init; }

    /// <summary>When the operator gave up - opened the lid, or pressed reset.</summary>
    public TimeSpan GaveUpAt { get; init; }

    public string GaveUpBy { get; init; } = string.Empty;

    /// <summary>Log lines between arriving and giving up. Zero is the loudest answer there is.</summary>
    public int EntriesWhileWaiting { get; init; }

    /// <summary>The cut mode in force, where the machine logs one.</summary>
    public string CutMode { get; init; } = string.Empty;

    public TimeSpan Waited => GaveUpAt - (InPositionAt ?? RequestedAt);

    public string Describe() => string.Join(", ", Targets);
}

public class CutNotTakenFindings
{
    /// <summary>Complete cut cycles in the log - the machine's own idea of a full one.</summary>
    public int CutCycles { get; init; }

    public TimeSpan? LastCutAt { get; init; }

    /// <summary>Positioning moves after the last cut that ended in the operator giving up.</summary>
    public IReadOnlyList<SilentWait> Waits { get; init; } = Array.Empty<SilentWait>();

    /// <summary>Every positioning move after the last cut, including the ones that were fine.</summary>
    public int MovesAfterLastCut { get; init; }

    /// <summary>
    /// Whether a two-hand control input appears anywhere in this log. Where it does not, the
    /// software cannot show whether the operator asked for the cut at all.
    /// </summary>
    public bool TwoHandEverLogged { get; init; }

    /// <summary>The cut mode each complete cut cycle began under, counted.</summary>
    public IReadOnlyDictionary<string, int> CutModeAtCutStart { get; init; } =
        new Dictionary<string, int>();

    /// <summary>The one cut mode in force across every wait, where they agree. Empty if they do not.</summary>
    public string CutModeDuringWaits { get; init; } = string.Empty;

    /// <summary>Cut modes this log never once made a cut under.</summary>
    public IReadOnlyList<string> CutModesThatNeverCut { get; init; } = Array.Empty<string>();

    /// <summary>
    /// One wait is a moment. Two or more is a pattern, and only a pattern is worth printing.
    /// <para>
    /// A saw being put away at the end of a shift leaves the same idle moves behind - the
    /// 100,000 line M20716 control log has five of them - and not one of those is retried.
    /// </para>
    /// </summary>
    public bool Any => Waits.Count >= EnoughToMeanSomething;

    internal const int EnoughToMeanSomething = 2;
}

/// <summary>
/// Finds the machine being driven into position with no cut following, over and over.
/// <para>
/// From a real M22215 (SprintM600) case at Akarana Timbers. The operator wrote "manual to 335
/// thntd, no action". The report of the day said the log ended on an axis status and the machine
/// had been stopped from the HMI - both true, neither any use. What the log actually shows is the
/// trolley reaching 339.1, then <b>thirteen seconds with not one line logged</b>, then the
/// operator opening the lid and trying again. Six times.
/// </para>
/// <para>
/// The silence is the finding. On that machine the two-hand buttons go straight into the PLC, so
/// the software only ever sees a press the PLC has already accepted. An operator pressing and
/// nothing happening looks exactly like nothing happening.
/// </para>
/// <para>
/// Checked against a 100,000 line M20716 saw log as a control, where a machine winding down at the
/// end of a shift produces the same five idle moves and none of them are retried. The retry is
/// what separates an operator fighting the machine from a machine being put away.
/// </para>
/// </summary>
public static class CutNotTakenCheck
{
    /// <summary>Cut steps this far apart belong to different cycles.</summary>
    private static readonly TimeSpan BetweenCycles = TimeSpan.FromSeconds(30);

    /// <summary>Shorter than this and the operator was repositioning, not waiting.</summary>
    private static readonly TimeSpan WorthReporting = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Axes commanded this close together are one request. A SprintM600 writes the trolley and
    /// the saw rotation about 26ms apart, and splitting them reports "SawRotation 90" as what the
    /// operator asked for when the target that matters is the trolley position.
    /// </summary>
    private static readonly TimeSpan SameRequest = TimeSpan.FromMilliseconds(250);

    private const string MoveTo = "Move to :";

    public static CutNotTakenFindings Check(IReadOnlyList<MachineLogEntry> entries)
    {
        if (entries.Count == 0) return new CutNotTakenFindings();

        var cutSteps = entries.Where(e => e.Tag.Equals("BladeCutStep", StringComparison.OrdinalIgnoreCase)).ToList();
        if (cutSteps.Count == 0) return new CutNotTakenFindings();

        var cycles = FullCutCycles(cutSteps);
        if (cycles.Count == 0) return new CutNotTakenFindings();

        var lastCut = cycles[^1][^1].Time;
        var requests = PositionRequests(entries).Where(r => r[0].Time > lastCut).ToList();

        var waits = new List<SilentWait>();

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var until = i + 1 < requests.Count ? requests[i + 1][0].Time : entries[^1].Time;

            // A cut did follow this one, so there is nothing to report about it.
            if (cutSteps.Any(c => c.Time > request[0].Time && c.Time <= until)) continue;
            if (until - request[0].Time < WorthReporting) continue;

            var gaveUp = GaveUp(entries, request[0].Time, until);
            if (gaveUp is null) continue;

            var arrived = entries
                .Where(e => e.Time > request[0].Time && e.Time <= gaveUp.Value.At)
                .FirstOrDefault(Arrived)?.Time;

            var from = arrived ?? request[0].Time;

            waits.Add(new SilentWait
            {
                RequestedAt = request[0].Time,
                Targets = request.Select(e => $"{e.Tag} {Target(e.Description)}".Trim()).ToList(),
                InPositionAt = arrived,
                GaveUpAt = gaveUp.Value.At,
                GaveUpBy = gaveUp.Value.How,
                EntriesWhileWaiting = entries.Count(e => e.Time > from && e.Time < gaveUp.Value.At),
                CutMode = CutModeAt(entries, request[0].Time)
            });
        }

        var atStart = cycles
            .Select(c => CutModeAt(entries, c[0].Time))
            .Where(mode => mode.Length > 0)
            .GroupBy(mode => mode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var everySeen = entries
            .Where(e => e.Tag.Equals("Cutmode", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Description.Trim())
            .Where(mode => mode.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duringWaits = waits.Select(w => w.CutMode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new CutNotTakenFindings
        {
            CutCycles = cycles.Count,
            LastCutAt = lastCut,
            Waits = waits,
            MovesAfterLastCut = requests.Count,
            TwoHandEverLogged = TwoHandControlCheck.EverLogged(entries),
            CutModeAtCutStart = atStart,
            CutModeDuringWaits = duringWaits.Count == 1 ? duringWaits[0] : string.Empty,
            CutModesThatNeverCut = everySeen.Where(m => !atStart.ContainsKey(m)).ToList()
        };
    }

    /// <summary>
    /// Cut cycles that ran all the way through.
    /// <para>
    /// The step numbers differ by machine - a SprintM600 runs 10, 20, 30, 40, 0 and the M20716 saw
    /// goes up to 60 - so the machine's own habit decides what a full one is. Bursts are grouped by
    /// time, and a burst counts as a cut when it reaches at least the median top step. That drops
    /// the short 5/7/12 bursts a SprintM600 logs when the blade is raised by hand, which are not
    /// cuts and would otherwise hide the very silence being looked for.
    /// </para>
    /// </summary>
    private static List<List<MachineLogEntry>> FullCutCycles(IReadOnlyList<MachineLogEntry> cutSteps)
    {
        var bursts = new List<List<MachineLogEntry>>();

        foreach (var step in cutSteps)
        {
            if (bursts.Count > 0 && step.Time - bursts[^1][^1].Time <= BetweenCycles)
                bursts[^1].Add(step);
            else
                bursts.Add(new List<MachineLogEntry> { step });
        }

        var tops = bursts.Select(TopStep).ToList();
        var sorted = tops.OrderBy(t => t).ToList();
        var median = sorted[sorted.Count / 2];

        return bursts.Where((_, i) => tops[i] >= median).ToList();
    }

    private static int TopStep(IEnumerable<MachineLogEntry> burst) => burst
        .Select(e => StepNumber(e.Description))
        .DefaultIfEmpty(-1)
        .Max();

    private static int StepNumber(string description)
    {
        var at = description.LastIndexOf('=');
        return at >= 0 && int.TryParse(description[(at + 1)..].Trim(), out var step) ? step : -1;
    }

    /// <summary>
    /// One command to move, however many axes it moved. A SprintM600 writes the trolley and the
    /// saw rotation on the same timestamp, and those are one request rather than two.
    /// </summary>
    private static List<List<MachineLogEntry>> PositionRequests(IReadOnlyList<MachineLogEntry> entries)
    {
        var requests = new List<List<MachineLogEntry>>();

        foreach (var entry in entries)
        {
            if (!entry.Description.StartsWith(MoveTo, StringComparison.OrdinalIgnoreCase)) continue;

            if (requests.Count > 0 && entry.Time - requests[^1][0].Time <= SameRequest)
                requests[^1].Add(entry);
            else
                requests.Add(new List<MachineLogEntry> { entry });
        }

        return requests;
    }

    private static string Target(string description) =>
        description[MoveTo.Length..].Trim();

    private static bool Arrived(MachineLogEntry entry) =>
        entry.Category == MachineLogCategory.MotionEvent
        && entry.Tag.Contains("Status", StringComparison.OrdinalIgnoreCase)
        && entry.Description.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The operator giving up: opening the lid, or pressing reset.
    /// <para>
    /// This is what separates a complaint from a machine being put away at the end of a shift. On
    /// the M20716 control log the wind-down produces the same idle moves and not one of them is
    /// retried; an operator fighting the machine retries every time.
    /// </para>
    /// </summary>
    private static (TimeSpan At, string How)? GaveUp(
        IReadOnlyList<MachineLogEntry> entries, TimeSpan from, TimeSpan until)
    {
        foreach (var entry in entries)
        {
            if (entry.Time <= from || entry.Time > until) continue;
            if (entry.Category != MachineLogCategory.InputChange) continue;
            if (IoAddress.ChangedTo(entry.Description) != 1) continue;

            if (entry.Tag.Contains("LidOpen", StringComparison.OrdinalIgnoreCase))
                return (entry.Time, "the lid was opened");

            if (entry.Tag.Contains("Reset", StringComparison.OrdinalIgnoreCase))
                return (entry.Time, "reset was pressed");
        }

        return null;
    }

    private static string CutModeAt(IReadOnlyList<MachineLogEntry> entries, TimeSpan moment)
    {
        var mode = string.Empty;

        foreach (var entry in entries)
        {
            if (entry.Time > moment) break;
            if (entry.Tag.Equals("Cutmode", StringComparison.OrdinalIgnoreCase)) mode = entry.Description.Trim();
        }

        return mode;
    }
}
