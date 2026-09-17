namespace DiagFileMonitor.Core.SpidaLogs;

/// <summary>
/// What a normal attempt looks like on this machine, in this log.
/// <para>
/// The report reads the end of the log closely, which is right - the file is exported minutes
/// after the problem. But reading the end closely without knowing what the middle looks like is
/// how an ordinary pause gets written up as a symptom. A machine that takes 90 seconds a unit all
/// morning and then takes 95 is fine. One that takes 90 all morning and then sits for 11 minutes
/// is not, and the only thing that tells you which is which is the rest of the file.
/// </para>
/// <para>
/// Everything here is measured from this one log. It is this machine against itself on this day,
/// not against any other machine and not against a specification.
/// </para>
/// </summary>
public class CycleBaseline
{
    /// <summary>Attempts that finished, which is what "normal" is measured from.</summary>
    public int Completed { get; init; }

    public int Attempted { get; init; }

    /// <summary>Middle completed attempt by duration. Median, so one stoppage does not move it.</summary>
    public TimeSpan Typical { get; init; }

    /// <summary>The quickest and slowest quarter marks, which say how steady the machine is.</summary>
    public TimeSpan Quickest { get; init; }
    public TimeSpan Slowest { get; init; }

    /// <summary>The last attempt, for comparing against the typical one.</summary>
    public TimeSpan? Last { get; init; }

    public bool LastCompleted { get; init; }

    /// <summary>How the last attempt compares with the typical one, as a multiple.</summary>
    public double? LastAgainstTypical => Typical > TimeSpan.Zero && Last is { } last
        ? last.TotalSeconds / Typical.TotalSeconds
        : null;

    /// <summary>Attempts that carried at least one machine fault.</summary>
    public int WithFaults { get; init; }

    /// <summary>
    /// Whether the machine was running normally before the end. Where it was not, the end of the
    /// log is not where the story starts and the report says so.
    /// </summary>
    public bool WasRunningNormally => Completed >= 3 && Typical > TimeSpan.Zero;

    public bool Any => Attempted > 0;

    public static CycleBaseline? From(IReadOnlyList<MachineCycle> cycles)
    {
        if (cycles.Count == 0) return null;

        // Only completed attempts describe normal. An attempt that was abandoned says how the day
        // went wrong, not how it goes right.
        var done = cycles.Where(c => c.Completed).Select(c => c.Duration).OrderBy(d => d).ToList();

        var last = cycles[^1];

        return new CycleBaseline
        {
            Attempted = cycles.Count,
            Completed = done.Count,
            Typical = done.Count > 0 ? done[done.Count / 2] : TimeSpan.Zero,
            Quickest = done.Count > 0 ? done[done.Count / 4] : TimeSpan.Zero,
            Slowest = done.Count > 0 ? done[(done.Count * 3) / 4] : TimeSpan.Zero,
            Last = last.Duration,
            LastCompleted = last.Completed,
            WithFaults = cycles.Count(c => c.MachineFaults.Count > 0)
        };
    }
}
