using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>What the machine was sitting on when the log ran out, and whether that is unusual.</summary>
public record StuckStep(
    int Step,
    TimeSpan ReachedAt,
    TimeSpan HeldFor,
    int TimesReachedInLog,
    TimeSpan TypicalHold,
    int SamplesForTypical,
    string Message,
    int RepeatsOfMessage)
{
    /// <summary>A step reached once, at the very end, is the branch the machine does not normally take.</summary>
    public bool FirstTimeToday => TimesReachedInLog == 1;

    /// <summary>Held for far longer than this machine holds that step when it is running.</summary>
    public bool HeldFarTooLong =>
        SamplesForTypical >= 3
        && TypicalHold > TimeSpan.Zero
        && HeldFor > TimeSpan.FromTicks(TypicalHold.Ticks * 20);

    /// <summary>Worth putting in front of a technician at all.</summary>
    public bool WorthReporting =>
        HeldFor >= TimeSpan.FromSeconds(30) && (FirstTimeToday || HeldFarTooLong);
}

/// <summary>
/// Reads the last step the sequencer reached and how long it stayed there.
/// <para>
/// This is the shape of a latched interlock, and it is what a bare fault count misses. A machine
/// that checks a guard 39 times in a shift and clears each one in two tenths of a second is
/// working; the same message once, with the machine still on that step four minutes later, is the
/// callout. Counting the message gets that exactly backwards - the healthy machine looks worse.
/// </para>
/// <para>
/// So the measure is dwell, not frequency: how long it sat there, against how long it normally
/// sits there, and whether it had ever taken that branch before today.
/// </para>
/// </summary>
public static class StuckStepCheck
{
    private static readonly Regex StepPattern =
        new(@"Step\s*=\s*(?<value>-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static StuckStep? Check(IReadOnlyList<MachineLogEntry> entries)
    {
        if (entries.Count == 0) return null;

        var steps = new List<(int Step, TimeSpan Time)>();

        foreach (var entry in entries)
        {
            var match = StepPattern.Match($"{entry.Tag} {entry.Description}");
            if (match.Success && int.TryParse(match.Groups["value"].Value, out var value))
                steps.Add((value, entry.Time));
        }

        if (steps.Count == 0) return null;

        var end = entries[^1].Time;
        var last = steps[^1];

        // How long it sat on that step every other time it reached it today.
        var holds = new List<TimeSpan>();
        for (var i = 0; i < steps.Count - 1; i++)
            if (steps[i].Step == last.Step)
            {
                var held = steps[i + 1].Time - steps[i].Time;
                if (held >= TimeSpan.Zero) holds.Add(held);
            }

        holds.Sort();
        var typical = holds.Count > 0 ? holds[holds.Count / 2] : TimeSpan.Zero;

        // Whatever the machine kept saying while it sat there.
        var after = entries.Where(e => e.Time >= last.Time && e.Description.Length > 0).ToList();
        var loudest = after
            .GroupBy(e => e.Description, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return new StuckStep(
            last.Step,
            last.Time,
            end - last.Time,
            steps.Count(s => s.Step == last.Step),
            typical,
            holds.Count,
            loudest?.Key ?? string.Empty,
            loudest?.Count() ?? 0);
    }
}
