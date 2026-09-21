using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One guard, how often it stopped the machine and what that cost.</summary>
public record GuardStop(
    string Name,
    string WhatItIs,
    string HowItClears,
    IReadOnlyList<TimeSpan> Waits)
{
    public int Count => Waits.Count;

    public TimeSpan Total => TimeSpan.FromTicks(Waits.Sum(w => w.Ticks));

    public TimeSpan Longest => Waits.Count == 0 ? TimeSpan.Zero : Waits.Max();

    public TimeSpan Typical
    {
        get
        {
            if (Waits.Count == 0) return TimeSpan.Zero;
            var sorted = Waits.OrderBy(w => w).ToList();
            return sorted[sorted.Count / 2];
        }
    }
}

public record GuardLedger(IReadOnlyList<GuardStop> Guards, TimeSpan LogCovers)
{
    public bool Any => Guards.Any(g => g.Count > 0);

    public TimeSpan Total => TimeSpan.FromTicks(Guards.Sum(g => g.Total.Ticks));

    public int Count => Guards.Sum(g => g.Count);

    public double? ShareOfShift => LogCovers > TimeSpan.FromMinutes(10)
        ? Total.TotalSeconds / LogCovers.TotalSeconds
        : null;

    public IReadOnlyList<GuardStop> Used => Guards.Where(g => g.Count > 0).ToList();
}

/// <summary>
/// Adds up what the machine's guards cost in time.
/// <para>
/// None of these are faults. Each one is the machine refusing to move until a person has dealt
/// with something, and every second spent on them is a second not spent crashing the head into
/// an obstruction or moving with somebody leaning on the bar. They are worth having.
/// </para>
/// <para>
/// But they are not free either, and "how much of the day goes on this" is a fair question from
/// a customer. So they get counted and timed rather than listed as faults. Counting the log
/// lines would be worse than useless: the PLC repeats a prompt while it waits, so the loudest
/// guard in the log is usually the cheapest one.
/// </para>
/// </summary>
public static class GuardStopLedger
{
    private const string ClearOfMovingParts = "Clear Of Any Moving Parts";
    private const string SafetyBar = "Safety Bar Pressed";
    private const string SafetyBarInput = "SafetyBarPressed";
    private const string TwoHandControl = "THNTD";

    public static GuardLedger Check(IReadOnlyList<MachineLogEntry> entries, FloatingHeadFindings floatingHead)
    {
        if (entries.Count == 0) return new GuardLedger(Array.Empty<GuardStop>(), TimeSpan.Zero);

        var covers = entries[^1].Time - entries[0].Time;

        return new GuardLedger(
            new[]
            {
                new GuardStop(
                    "Floating head obstruction",
                    "The laser stopping the head before it drives into whatever is in front of it - "
                    + "usually the pieces set by hand for a taller panel, still in the way of a shorter one.",
                    "Operator moves the pieces and presses THNTD.",
                    floatingHead.Episodes.Select(e => e.Lasted).ToList()),
                SafetyBarStops(entries),
                ClearOfMovingPartsStops(entries)
            },
            covers);
    }

    /// <summary>
    /// Timed off the bar input rather than the message, because the input says when the bar was
    /// actually let go. It runs to the machine's next sequencer step, which is when it is moving
    /// again - the servos all drop out on a bar press and have to come back.
    /// </summary>
    private static GuardStop SafetyBarStops(IReadOnlyList<MachineLogEntry> entries)
    {
        var pressed = entries
            .Where(e => e.Tag.Contains(SafetyBarInput, StringComparison.OrdinalIgnoreCase)
                        && e.Description.TrimEnd().EndsWith("1", StringComparison.Ordinal))
            .Select(e => e.Time)
            .ToList();

        // A bundle whose log does not carry the input still has the message to go on.
        if (pressed.Count == 0)
            pressed = entries
                .Where(e => e.Description.Contains(SafetyBar, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Time)
                .ToList();

        var released = entries
            .Where(e => e.Tag.Contains(SafetyBarInput, StringComparison.OrdinalIgnoreCase)
                        && e.Description.TrimEnd().EndsWith("0", StringComparison.Ordinal))
            .Select(e => e.Time)
            .ToList();

        var steps = entries
            .Where(e => e.Description.Contains("Step =", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Time)
            .ToList();

        var waits = new List<TimeSpan>();
        var doneTo = TimeSpan.MinValue;

        foreach (var start in pressed.OrderBy(t => t))
        {
            if (start <= doneTo) continue;

            var letGo = released.FirstOrDefault(t => t > start);
            var from = letGo > start ? letGo : start;
            var movingAgain = steps.FirstOrDefault(t => t > from);

            if (movingAgain <= from) continue;

            waits.Add(movingAgain - start);
            doneTo = movingAgain;
        }

        return new GuardStop(
            "Floating side safety bar",
            "Somebody leaning on the bar. Every servo drops out, so nothing can move until it is "
            + "let go and reset.",
            "Let the bar go, press E-Stop reset.",
            waits);
    }

    /// <summary>
    /// The prompt before the machine moves. It is answered by a THNTD press, so the wait is the
    /// operator's response time and nothing else.
    /// </summary>
    private static GuardStop ClearOfMovingPartsStops(IReadOnlyList<MachineLogEntry> entries)
    {
        var prompts = entries
            .Where(e => e.Description.Contains(ClearOfMovingParts, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Time)
            .ToList();

        var presses = entries
            .Where(e => e.Category == MachineLogCategory.InputChange
                        && e.Tag.Contains(TwoHandControl, StringComparison.OrdinalIgnoreCase)
                        && e.Description.TrimEnd().EndsWith("1", StringComparison.Ordinal))
            .Select(e => e.Time)
            .ToList();

        var waits = new List<TimeSpan>();
        var doneTo = TimeSpan.MinValue;

        foreach (var prompt in prompts.OrderBy(t => t))
        {
            if (prompt <= doneTo) continue;

            var answered = presses.FirstOrDefault(t => t >= prompt);
            if (answered < prompt) continue;

            waits.Add(answered - prompt);
            doneTo = answered;
        }

        return new GuardStop(
            "\"Clear of any moving parts\" prompt",
            "The machine asking whether everyone is clear before it moves.",
            "Operator presses the clamp/fire buttons.",
            waits);
    }
}
