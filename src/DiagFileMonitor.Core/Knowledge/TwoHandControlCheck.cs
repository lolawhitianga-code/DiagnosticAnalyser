using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One press of the two-hand control: when it went down, when it came up, how long it was held.</summary>
public class TwoHandPress
{
    public TimeSpan PressedAt { get; init; }
    public TimeSpan? ReleasedAt { get; init; }

    public TimeSpan? Held => ReleasedAt is { } released ? released - PressedAt : null;

    /// <summary>True where the press was still down when the log ended.</summary>
    public bool StillHeld => ReleasedAt is null;
}

/// <summary>One command to fire, with every gun output that went on together.</summary>
public class GunFiring
{
    public TimeSpan At { get; init; }
    public IReadOnlyList<string> Outputs { get; init; } = Array.Empty<string>();
    public TimeSpan? HeldFor { get; init; }

    public string Describe() => $"{At:hh\\:mm\\:ss\\.fff}  "
                                + string.Join(", ", Outputs)
                                + (HeldFor is { } held ? $"  (on for {held.TotalMilliseconds:0}ms)" : string.Empty);
}

public class TwoHandControlFindings
{
    /// <summary>The input tag, as the log spells it. THNTD on the raked wall extruders.</summary>
    public string InputTag { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public IReadOnlyList<TwoHandPress> Presses { get; init; } = Array.Empty<TwoHandPress>();

    public TwoHandPress? LastPress => Presses.Count > 0 ? Presses[^1] : null;

    /// <summary>What the machine did in the seconds after the last release. The operator asked for
    /// something; this is the answer to what it did about it.</summary>
    public IReadOnlyList<MachineLogEntry> AfterLastRelease { get; init; } = Array.Empty<MachineLogEntry>();

    /// <summary>Every commanded firing in the log.</summary>
    public IReadOnlyList<GunFiring> Firings { get; init; } = Array.Empty<GunFiring>();

    /// <summary>
    /// A firing commanded after the last press was released. Where this is empty and the operator
    /// says a gun went off, the log is saying the PLC never asked it to - which points at the
    /// valve or the air side, not the control side.
    /// </summary>
    public IReadOnlyList<GunFiring> FiringsAfterLastPress { get; init; } = Array.Empty<GunFiring>();

    /// <summary>The typical hold, so an unusually short press stands out against this machine's own habit.</summary>
    public TimeSpan? MedianHold { get; init; }

    public bool Any => Presses.Count > 0 || Firings.Count > 0;
}

/// <summary>
/// Reads the two-hand control and what followed it.
/// <para>
/// On a raked wall extruder the operator drives the firing sequence with a two-hand no-tie-down
/// control, logged as the <c>THNTD</c> input. The documented sequence is three presses: clamp the
/// plates, clamp the studs, then fire. So the last press before the machine stopped is the last
/// thing the operator asked for, and what happened after it is the machine's answer.
/// </para>
/// <para>
/// Confirmed against a real AOR1694 export where an operator reported a gun firing on its own.
/// </para>
/// </summary>
public static class TwoHandControlCheck
{
    /// <summary>How long after the last release to keep reporting what happened.</summary>
    private static readonly TimeSpan FollowWindow = TimeSpan.FromSeconds(10);

    private static readonly string[] TwoHandTags = { "THNTD" };

    /// <summary>
    /// Whether this machine logs a two-hand control at all.
    /// <para>
    /// Silence here is not "nothing to report". On a SprintM600 the buttons go straight into the
    /// PLC and the software only ever sees a press the PLC has accepted, so an operator pressing
    /// and getting nothing leaves no trace whatsoever. Saying so is the whole point.
    /// </para>
    /// </summary>
    public static bool EverLogged(IReadOnlyList<MachineLogEntry> entries) => entries
        .Any(e => e.Category == MachineLogCategory.InputChange
                  && TwoHandTags.Contains(e.Tag, StringComparer.OrdinalIgnoreCase));

    public static TwoHandControlFindings Check(IReadOnlyList<MachineLogEntry> entries)
    {
        if (entries.Count == 0) return new TwoHandControlFindings();

        var tag = entries
            .Where(e => e.Category == MachineLogCategory.InputChange)
            .Select(e => e.Tag)
            .FirstOrDefault(t => TwoHandTags.Contains(t, StringComparer.OrdinalIgnoreCase));

        var presses = tag is null ? new List<TwoHandPress>() : ReadPresses(entries, tag);
        var firings = ReadFirings(entries);

        var address = tag is null
            ? string.Empty
            : IoAddress.From(entries.FirstOrDefault(e => e.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))
                ?.Description ?? string.Empty);

        var last = presses.Count > 0 ? presses[^1] : null;
        var from = last?.ReleasedAt ?? last?.PressedAt;

        var after = from is { } start
            ? entries.Where(e => e.Time > start && e.Time <= start + FollowWindow).ToList()
            : new List<MachineLogEntry>();

        var holds = presses.Where(p => p.Held is not null).Select(p => p.Held!.Value).OrderBy(h => h).ToList();

        return new TwoHandControlFindings
        {
            InputTag = tag ?? string.Empty,
            Address = address,
            Presses = presses,
            AfterLastRelease = after,
            Firings = firings,
            FiringsAfterLastPress = from is { } t ? firings.Where(f => f.At > t).ToList() : firings,
            MedianHold = holds.Count > 0 ? holds[holds.Count / 2] : null
        };
    }

    private static List<TwoHandPress> ReadPresses(IReadOnlyList<MachineLogEntry> entries, string tag)
    {
        var presses = new List<TwoHandPress>();
        TimeSpan? down = null;

        foreach (var entry in entries)
        {
            if (entry.Category != MachineLogCategory.InputChange) continue;
            if (!entry.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) continue;

            var value = IoAddress.ChangedTo(entry.Description);
            if (value is null) continue;

            if (value == 1)
            {
                // A second 1 without a 0 between is the same press still down.
                down ??= entry.Time;
            }
            else if (down is { } pressedAt)
            {
                presses.Add(new TwoHandPress { PressedAt = pressedAt, ReleasedAt = entry.Time });
                down = null;
            }
        }

        if (down is { } stillDown) presses.Add(new TwoHandPress { PressedAt = stillDown });

        return presses;
    }

    /// <summary>
    /// Gun outputs going on together are one firing. They are commanded at the same instant and
    /// released together a fraction of a second later, so grouping by timestamp is what turns
    /// eight output lines into the two events they really are.
    /// </summary>
    private static List<GunFiring> ReadFirings(IReadOnlyList<MachineLogEntry> entries)
    {
        var firings = new List<GunFiring>();

        var gunLines = entries
            .Where(e => e.Category == MachineLogCategory.OutputChange)
            .Where(e => e.Tag.Contains("GunFire", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var on = gunLines.Where(e => IoAddress.SetOn(e.Description)).GroupBy(e => e.Time);
        var offTimes = gunLines.Where(e => IoAddress.SetOff(e.Description)).Select(e => e.Time).ToList();

        foreach (var group in on.OrderBy(g => g.Key))
        {
            var off = offTimes.Where(t => t > group.Key).Cast<TimeSpan?>().FirstOrDefault();

            firings.Add(new GunFiring
            {
                At = group.Key,
                Outputs = group.Select(e => $"{e.Tag} {IoAddress.From(e.Description)}".Trim())
                    .Distinct()
                    .OrderBy(o => o, StringComparer.Ordinal)
                    .ToList(),
                HeldFor = off is { } offAt ? offAt - group.Key : null
            });
        }

        return firings;
    }
}

/// <summary>Small readers for the shapes an IO line is written in.</summary>
internal static class IoAddress
{
    /// <summary>The COM address out of "Input (COM7-2.12) Changed to 1".</summary>
    public static string From(string description)
    {
        var open = description.IndexOf('(');
        var close = description.IndexOf(')');
        return open >= 0 && close > open ? description[(open + 1)..close] : string.Empty;
    }

    public static int? ChangedTo(string description)
    {
        var at = description.LastIndexOf("Changed to ", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        return int.TryParse(description[(at + "Changed to ".Length)..].Trim(), out var value) ? value : null;
    }

    public static bool SetOn(string description) =>
        description.EndsWith("Set On", StringComparison.OrdinalIgnoreCase);

    public static bool SetOff(string description) =>
        description.EndsWith("Set Off", StringComparison.OrdinalIgnoreCase);
}
