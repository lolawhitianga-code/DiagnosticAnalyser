using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>What a step normally leads to, against what it did the last time.</summary>
public class StepOutcome
{
    public int Step { get; init; }

    /// <summary>How many times the log reached this step.</summary>
    public int Occurrences { get; init; }

    /// <summary>The step it most often went on to, and how many times.</summary>
    public int? UsualNextStep { get; init; }
    public int UsualNextCount { get; init; }

    /// <summary>What it went to the last time.</summary>
    public int? FinalNextStep { get; init; }

    /// <summary>How long it sat there the last time, and how long it usually sits.</summary>
    public TimeSpan? FinalDwell { get; init; }
    public TimeSpan? TypicalDwell { get; init; }

    public TimeSpan FinalReachedAt { get; init; }

    /// <summary>
    /// The last time round it did something different from its habit. That is the whole point of
    /// this check - the machine's own history is the benchmark, so it works on a step whose meaning
    /// nobody has written down.
    /// </summary>
    public bool FinalDifferedFromUsual =>
        Occurrences >= 3
        // No next step means the log simply ended here. That is not the machine doing something
        // different, and calling it a deviation would flag the end of every export.
        && FinalNextStep is not null
        && UsualNextStep is not null
        && FinalNextStep != UsualNextStep;
}

public class StepStoryFindings
{
    /// <summary>The step tag this reads, e.g. WallExtruderStep. The busiest one in the log.</summary>
    public string StepTag { get; init; } = string.Empty;

    /// <summary>Other step counters in the same log, which run independently of the main one.</summary>
    public IReadOnlyList<string> OtherStepTags { get; init; } = Array.Empty<string>();

    /// <summary>The last step the machine was working through before it ended up where it ended up.</summary>
    public StepOutcome? LastWorkingStep { get; init; }

    /// <summary>
    /// The operator stopped the machine from the HMI. On the wall extruder this is
    /// <c>WallExtruderStep Step = 0</c> arriving from a high step - the machine did not finish a
    /// cycle, somebody stopped it. A <c>SidePLCStep Step = 0</c> is a different counter resetting
    /// normally and is not this.
    /// </summary>
    public bool OperatorStoppedFromHmi { get; init; }

    public TimeSpan? StoppedAt { get; init; }

    /// <summary>How sure we are that a zero on this tag means an operator stop.</summary>
    public Confidence StopConfidence { get; init; } = Confidence.Inferred;

    /// <summary>Everything the machine shut down in the moment after the stop, as one event.</summary>
    public IReadOnlyList<MachineLogEntry> ShutdownCascade { get; init; } = Array.Empty<MachineLogEntry>();

    /// <summary>
    /// True where there is something worth saying. A log carrying a single step reading is not
    /// that - there is nothing to compare it against, so it is not a finding.
    /// </summary>
    public bool Any => OperatorStoppedFromHmi
                       || (LastWorkingStep is { } step && step.Occurrences > 1);
}

/// <summary>
/// Compares the last thing the machine did at a step against every other time it was at that step.
/// <para>
/// This needs no knowledge of what the step numbers mean, which is the point - nobody has decoded
/// them yet. If a step went to 1400 five times and to 0 on the sixth, that sixth time is worth a
/// look whatever 1400 turns out to be.
/// </para>
/// </summary>
public static class StepOutcomeCheck
{
    /// <summary>Everything logged inside this window after a stop is the machine turning off.</summary>
    private static readonly TimeSpan CascadeWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Step tags whose zero is known to be an operator stop rather than a counter resetting.
    /// Confirmed on the raked wall extruder by the technician who read the export.
    /// </summary>
    private static readonly string[] ZeroMeansOperatorStop = { "WallExtruderStep" };

    public static StepStoryFindings Check(IReadOnlyList<MachineLogEntry> entries)
    {
        if (entries.Count == 0) return new StepStoryFindings();

        var byTag = entries
            .Where(e => e.Category == MachineLogCategory.Other)
            .Where(e => MachineCycles.ReadStep(e) is not null)
            .GroupBy(e => e.Tag)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (byTag.Count == 0) return new StepStoryFindings();

        // The busiest counter is the one driving the machine. A second counter in the same log is
        // a different PLC keeping its own count, and mixing the two would invent transitions that
        // never happened.
        var main = byTag[0];
        var readings = main
            .Select(e => (Time: e.Time, Step: MachineCycles.ReadStep(e)!.Value))
            .OrderBy(r => r.Time)
            .ToList();

        var stoppedFromHmi = false;
        TimeSpan? stoppedAt = null;
        var cascade = new List<MachineLogEntry>();

        // A zero arriving from a high step is somebody stopping the machine part way through.
        if (readings.Count >= 2 && readings[^1].Step == 0 && readings[^2].Step > 0)
        {
            stoppedFromHmi = true;
            stoppedAt = readings[^1].Time;
            cascade = entries
                .Where(e => e.Time >= stoppedAt.Value && e.Time <= stoppedAt.Value + CascadeWindow)
                .ToList();
        }

        // The step it was working through - the one before the stop, or simply the last one.
        var workingIndex = stoppedFromHmi ? readings.Count - 2 : readings.Count - 1;

        return new StepStoryFindings
        {
            StepTag = main.Key,
            OtherStepTags = byTag.Skip(1).Select(g => g.Key).ToList(),
            LastWorkingStep = workingIndex >= 0 ? Describe(readings, workingIndex) : null,
            OperatorStoppedFromHmi = stoppedFromHmi,
            StoppedAt = stoppedAt,
            StopConfidence = ZeroMeansOperatorStop.Contains(main.Key, StringComparer.OrdinalIgnoreCase)
                ? Confidence.Confirmed
                : Confidence.Inferred,
            ShutdownCascade = cascade
        };
    }

    private static StepOutcome Describe(List<(TimeSpan Time, int Step)> readings, int index)
    {
        var step = readings[index].Step;

        var nexts = new List<int>();
        var dwells = new List<TimeSpan>();

        for (var i = 0; i < readings.Count - 1; i++)
        {
            if (readings[i].Step != step) continue;
            if (i == index) continue;

            nexts.Add(readings[i + 1].Step);
            dwells.Add(readings[i + 1].Time - readings[i].Time);
        }

        var usual = nexts.GroupBy(n => n).OrderByDescending(g => g.Count()).FirstOrDefault();
        dwells.Sort();

        return new StepOutcome
        {
            Step = step,
            Occurrences = readings.Count(r => r.Step == step),
            UsualNextStep = usual?.Key,
            UsualNextCount = usual?.Count() ?? 0,
            FinalNextStep = index + 1 < readings.Count ? readings[index + 1].Step : null,
            FinalDwell = index + 1 < readings.Count ? readings[index + 1].Time - readings[index].Time : null,
            TypicalDwell = dwells.Count > 0 ? dwells[dwells.Count / 2] : null,
            FinalReachedAt = readings[index].Time
        };
    }
}
