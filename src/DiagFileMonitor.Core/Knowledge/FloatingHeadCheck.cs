using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One run of the machine refusing to bring the floating head in.</summary>
public record ObstructionEpisode(
    TimeSpan StartedAt,
    TimeSpan LastComplaintAt,
    int Complaints,
    double? HeightBefore,
    double? HeightAfter,
    bool ClearedAndCarriedOn,
    bool LogEndedDuringIt)
{
    /// <summary>Negative when the head was being asked to come in to a lower panel.</summary>
    public double? HeightChange =>
        HeightBefore is { } b && HeightAfter is { } a ? a - b : null;

    /// <summary>
    /// The ordinary case: the next panel is shorter, the hand-set pieces for the taller one are
    /// still in the way, and the operator moves them.
    /// </summary>
    public bool ExplainedByALowerPanel => HeightChange is { } change && change < -1;

    public TimeSpan Lasted => LastComplaintAt - StartedAt;

    /// <summary>
    /// Cleared quickly and the machine carried on. Whatever we can or cannot say about heights,
    /// that is the machine working - the first move in of a shift lands here, with no earlier
    /// height target to compare against.
    /// </summary>
    public bool SortedItselfOut => ClearedAndCarriedOn && Lasted < TimeSpan.FromMinutes(1);
}

public record FloatingHeadFindings(IReadOnlyList<ObstructionEpisode> Episodes)
{
    public bool Any => Episodes.Count > 0;

    /// <summary>Episodes with no height drop to explain them, or that never cleared.</summary>
    public IReadOnlyList<ObstructionEpisode> WorthALook => Episodes
        .Where(e => !Routine.Contains(e))
        .ToList();

    /// <summary>The ones that need no explaining, either by the height or by how fast they went.</summary>
    public IReadOnlyList<ObstructionEpisode> Routine => Episodes
        .Where(e => (e.ExplainedByALowerPanel || e.SortedItselfOut) && !e.LogEndedDuringIt)
        .ToList();
}

/// <summary>
/// Reads the "Unsafe to move Floating Head please clear Obstacle" message properly.
/// <para>
/// This message is <b>normally not a fault</b>. Going from a taller panel to a shorter one, the
/// floating head has to come in, and the pieces the operator set by hand for the taller panel are
/// still standing there. The laser sees them, the machine stops instead of driving into them, the
/// operator moves them and presses THNTD. That is the machine working.
/// </para>
/// <para>
/// Two things made this easy to get wrong. The PLC polls while it is blocked, alternating steps
/// 320 and 321 about every 0.18s, so a single wait shows up as a run of identical lines and the
/// gap between two of them is the poll interval, not how long the machine was held up. And the
/// message says nothing about height, so the only way to tell a routine wait from a real one is
/// to read the floating head's target either side of it. On an M21737 log all nine episodes sat
/// across a height reduction, from 52mm to 1.9m.
/// </para>
/// <para>
/// So what this reports is the exception: an episode with no height drop behind it, or one the
/// log ends in the middle of.
/// </para>
/// </summary>
public static class FloatingHeadCheck
{
    private const string Obstruction = "Unsafe to move Floating Head";

    /// <summary>
    /// How long after the last complaint the log may end and still count as ending inside it.
    /// </summary>
    private static readonly TimeSpan EndsInsideIt = TimeSpan.FromMinutes(10);

    private static readonly Regex HeightTarget = new(
        @"Move\s*to\s*:\s*(?<value>-?[\d.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static FloatingHeadFindings Check(IReadOnlyList<MachineLogEntry> entries)
    {
        if (entries.Count == 0) return new FloatingHeadFindings(Array.Empty<ObstructionEpisode>());

        var heights = new List<(TimeSpan Time, double Value)>();
        var complaints = new List<TimeSpan>();
        var steps = new List<(TimeSpan Time, int Step)>();

        foreach (var entry in entries)
        {
            if (entry.Tag.Equals("FloatingSideHeight", StringComparison.OrdinalIgnoreCase)
                && HeightTarget.Match(entry.Description) is { Success: true } target
                && double.TryParse(target.Groups["value"].Value, out var value))
                heights.Add((entry.Time, value));

            if (entry.Description.Contains(Obstruction, StringComparison.OrdinalIgnoreCase))
                complaints.Add(entry.Time);

            var step = StepPattern.Match($"{entry.Tag} {entry.Description}");
            if (step.Success && int.TryParse(step.Groups["value"].Value, out var stepValue))
                steps.Add((entry.Time, stepValue));
        }

        if (complaints.Count == 0) return new FloatingHeadFindings(Array.Empty<ObstructionEpisode>());

        var end = entries[^1].Time;
        var episodes = new List<ObstructionEpisode>();

        // One wait runs until the machine gets out of the 320/321 poll. Splitting on a time gap
        // would cut a four minute wait into three, because the HMI only repeats the message when
        // something else happens - here twice in four minutes, two minutes apart.
        var start = complaints[0];
        var last = complaints[0];
        var count = 1;

        for (var i = 1; i <= complaints.Count; i++)
        {
            if (i < complaints.Count && !LeftThePoll(steps, last, complaints[i]))
            {
                last = complaints[i];
                count++;
                continue;
            }

            episodes.Add(Build(start, last, count, heights, steps, end));

            if (i < complaints.Count)
            {
                start = complaints[i];
                last = complaints[i];
                count = 1;
            }
        }

        return new FloatingHeadFindings(episodes);
    }

    /// <summary>Did the sequencer get on with something else between these two complaints?</summary>
    private static bool LeftThePoll(
        List<(TimeSpan Time, int Step)> steps, TimeSpan from, TimeSpan to) =>
        steps.Any(s => s.Time > from && s.Time < to && s.Step != 320 && s.Step != 321);

    private static readonly Regex StepPattern =
        new(@"Step\s*=\s*(?<value>-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static ObstructionEpisode Build(
        TimeSpan start, TimeSpan last, int count,
        List<(TimeSpan Time, double Value)> heights,
        List<(TimeSpan Time, int Step)> steps,
        TimeSpan end)
    {
        double? before = heights.LastOrDefault(h => h.Time <= start) is { Time.Ticks: > 0 } b ? b.Value : null;
        double? after = heights.FirstOrDefault(h => h.Time > last) is { Time.Ticks: > 0 } a ? a.Value : null;

        // Getting past it means leaving the 320/321 poll for the rest of the eject sequence.
        var carriedOn = steps.Any(s => s.Time > last && s.Step != 320 && s.Step != 321);

        return new ObstructionEpisode(
            start, last, count, before, after, carriedOn,
            LogEndedDuringIt: !carriedOn && end - last < EndsInsideIt);
    }
}
