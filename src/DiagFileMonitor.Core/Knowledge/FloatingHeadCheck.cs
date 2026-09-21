using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One run of the laser holding the floating head back, and what it cost.</summary>
public record ObstructionEpisode(
    TimeSpan StartedAt,
    TimeSpan EndedAt,
    int Complaints,
    double? HeightBefore,
    double? HeightAfter,
    int? WentOnToStep,
    bool LogEndedDuringIt)
{
    /// <summary>Start to the moment the sequencer got on with something else.</summary>
    public TimeSpan Lasted => EndedAt - StartedAt;

    /// <summary>Negative when the head was being asked to come in to a lower panel.</summary>
    public double? HeightChange => HeightBefore is { } b && HeightAfter is { } a ? a - b : null;

    /// <summary>The ordinary case: next panel is shorter, hand-set pieces still in the way.</summary>
    public bool ExplainedByALowerPanel => HeightChange is { } change && change < -1;

    /// <summary>It cleared and the machine carried on with the eject.</summary>
    public bool Cleared => WentOnToStep is { } step && step != 0;

    /// <summary>Taken back to step 0 instead of being cleared - the operator gave up on it.</summary>
    public bool Abandoned => WentOnToStep == 0;
}

public record FloatingHeadFindings(
    IReadOnlyList<ObstructionEpisode> Episodes,
    TimeSpan LogCovers)
{
    public bool Any => Episodes.Count > 0;

    /// <summary>What the guard cost across the whole log. This is the number support wants.</summary>
    public TimeSpan TotalTime =>
        TimeSpan.FromTicks(Episodes.Sum(e => e.Lasted.Ticks));

    public TimeSpan Longest =>
        Episodes.Count == 0 ? TimeSpan.Zero : Episodes.Max(e => e.Lasted);

    public ObstructionEpisode? LongestEpisode =>
        Episodes.OrderByDescending(e => e.Lasted).FirstOrDefault();

    /// <summary>Share of the shift spent waiting on it, where the log is long enough to mean anything.</summary>
    public double? ShareOfShift => LogCovers > TimeSpan.FromMinutes(10)
        ? TotalTime.TotalSeconds / LogCovers.TotalSeconds
        : null;

    public int AbandonedCount => Episodes.Count(e => e.Abandoned);

    /// <summary>
    /// The few that are not simply the guard doing its job: no height drop behind them and slow
    /// enough to have cost real time, or one the log ends in the middle of.
    /// </summary>
    public IReadOnlyList<ObstructionEpisode> WorthALook => Episodes
        .Where(e => e.LogEndedDuringIt
                    || (!e.ExplainedByALowerPanel && e.Lasted >= TimeSpan.FromMinutes(1)))
        .ToList();
}

/// <summary>
/// Reads the "Unsafe to move Floating Head please clear Obstacle" message and measures what it
/// costs.
/// <para>
/// <b>This is not a fault to fix.</b> It is the laser stopping the head before it drives into
/// something - almost always the pieces the operator set by hand for a taller panel, still
/// standing in the way when the next panel is shorter. The operator moves them and presses
/// THNTD. Spending time on this beats the machine crashing into what the laser saw, which is a
/// safety matter, so the guard tripping is the system working as designed.
/// </para>
/// <para>
/// What is worth knowing is therefore the <b>time</b>: how many waits, how long in total, how
/// long the worst one, and how that compares with the shift. On the two Raked Wall Extruder V3
/// logs we have it comes to well under one percent of the day.
/// </para>
/// <para>
/// Two things make this easy to misread. The PLC polls while it is blocked, alternating steps
/// 320 and 321 about every 0.18s, so one wait writes a run of identical lines and counting them
/// turns a five second wait into thirty faults. And the message says nothing about height, so
/// the only way to tell a routine wait from an odd one is to read the floating head's target
/// either side of it.
/// </para>
/// </summary>
public static class FloatingHeadCheck
{
    private const string Obstruction = "Unsafe to move Floating Head";

    /// <summary>The steps the sequencer alternates between while the laser holds it back.</summary>
    private static readonly int[] PollSteps = { 320, 321 };

    /// <summary>How long after the last complaint the log may end and still count as inside it.</summary>
    private static readonly TimeSpan EndsInsideIt = TimeSpan.FromMinutes(10);

    private static readonly Regex HeightTarget = new(
        @"Move\s*to\s*:\s*(?<value>-?[\d.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StepPattern = new(
        @"Step\s*=\s*(?<value>-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static FloatingHeadFindings Check(IReadOnlyList<MachineLogEntry> entries)
    {
        if (entries.Count == 0)
            return new FloatingHeadFindings(Array.Empty<ObstructionEpisode>(), TimeSpan.Zero);

        var covers = entries[^1].Time - entries[0].Time;
        var heights = new List<(TimeSpan Time, double Value)>();
        var steps = new List<(TimeSpan Time, int Step)>();
        var starts = new List<TimeSpan>();
        var complaints = new List<TimeSpan>();

        foreach (var entry in entries)
        {
            if (entry.Tag.Equals("FloatingSideHeight", StringComparison.OrdinalIgnoreCase)
                && HeightTarget.Match(entry.Description) is { Success: true } target
                && double.TryParse(target.Groups["value"].Value, out var value))
                heights.Add((entry.Time, value));

            if (entry.Description.Contains(Obstruction, StringComparison.OrdinalIgnoreCase))
            {
                complaints.Add(entry.Time);
                starts.Add(entry.Time);
            }

            if (StepPattern.Match($"{entry.Tag} {entry.Description}") is { Success: true } step
                && int.TryParse(step.Groups["value"].Value, out var stepValue))
            {
                steps.Add((entry.Time, stepValue));
                if (stepValue == 321) starts.Add(entry.Time);
            }
        }

        if (complaints.Count == 0)
            return new FloatingHeadFindings(Array.Empty<ObstructionEpisode>(), covers);

        var end = entries[^1].Time;
        var episodes = new List<ObstructionEpisode>();
        var consumedTo = TimeSpan.MinValue;

        foreach (var start in starts.Distinct().OrderBy(t => t))
        {
            if (start <= consumedTo) continue;

            // The wait runs until the sequencer gets on with something that is not the poll.
            var next = steps.FirstOrDefault(s => s.Time > start && !PollSteps.Contains(s.Step));
            var over = next.Time > start;
            var finishedAt = over ? next.Time : end;

            episodes.Add(new ObstructionEpisode(
                start,
                finishedAt,
                complaints.Count(c => c >= start && c <= finishedAt),
                heights.LastOrDefault(h => h.Time <= start) is { Time.Ticks: > 0 } b ? b.Value : null,
                heights.FirstOrDefault(h => h.Time > finishedAt) is { Time.Ticks: > 0 } a ? a.Value : null,
                over ? next.Step : null,
                LogEndedDuringIt: !over && end - start < EndsInsideIt));

            consumedTo = finishedAt;
        }

        return new FloatingHeadFindings(episodes, covers);
    }
}
