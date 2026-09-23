using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One time the machine measured something and it was not where it should be.</summary>
/// <param name="SinceClampDown">How long after the clamps were sent down the check came, where they were.</param>
public record MeasurementFailure(TimeSpan At, double Expected, double Got, TimeSpan? SinceClampDown);

/// <summary>Every failure of one check, e.g. "Incorrect Nog Height From Top Of Stud".</summary>
public record FailedMeasurement(string What, IReadOnlyList<MeasurementFailure> Failures, string? SeenBefore)
{
    public int Count => Failures.Count;

    /// <summary>What it should have read, the value it asked for most.</summary>
    public double Expected => Failures.GroupBy(f => f.Expected).OrderByDescending(g => g.Count()).First().Key;

    public double LowestGot => Failures.Min(f => f.Got);
    public double HighestGot => Failures.Max(f => f.Got);

    /// <summary>Every reading on the same side of what it wanted - a thing that stops short, not noise.</summary>
    public bool AllShort => Failures.All(f => f.Got < f.Expected);

    public bool AllOver => Failures.All(f => f.Got > f.Expected);

    /// <summary>The check came at the same moment after the clamps went down every time - a set time running out.</summary>
    public TimeSpan? TypicalSinceClampDown
    {
        get
        {
            var times = Failures.Where(f => f.SinceClampDown is not null)
                .Select(f => f.SinceClampDown!.Value).OrderBy(t => t).ToList();
            return times.Count == 0 ? null : times[times.Count / 2];
        }
    }
}

public record MeasurementFindings(
    IReadOnlyList<FailedMeasurement> Failed,
    int GoodClampCycles,
    TimeSpan? TypicalClampToLock,
    TimeSpan? FastestClampToLock,
    TimeSpan? SlowestClampToLock,
    IReadOnlyList<ChangeLogEntry> ClampTimingChanges)
{
    public bool Any => Failed.Count > 0;

    public static MeasurementFindings None { get; } =
        new(Array.Empty<FailedMeasurement>(), 0, null, null, null, Array.Empty<ChangeLogEntry>());
}

/// <summary>
/// The machine's own measurement checks, and every time one failed.
/// <para>
/// Some machines measure a part before they commit to it and say so when it is wrong:
/// <c>Incorrect Nog Height From Top Of Stud, Expected : 45.0 Got: 12.2</c>. That is the machine
/// stating the problem in numbers, and it can sit unnoticed in a log among thousands of lines -
/// the M21868 report said nothing about 41 of them.
/// </para>
/// <para>
/// Where the check follows a clamp, the timing says more than the reading. On M21868 good cycles
/// locked the clamp about 1.2 s after sending it down; every failed check came at 2.62 s with no
/// lock logged before it. The cause, found on the machine: the nog clamp was coming down very
/// slowly from its upper position (about 200 mm) and was locked in place before it reached 45 mm.
/// Opening its flow control right up fixed it.
/// </para>
/// </summary>
public static class MeasurementCheck
{
    private static readonly Regex Failed = new(
        @"^(?<what>Incorrect\b[^,]*?)\s*,?\s*Expected\s*:\s*(?<e>-?\d+(?:\.\d+)?)\s*Got\s*:\s*(?<g>-?\d+(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>What a check's failure turned out to be on a machine where it was found. Keyed on the check's words.</summary>
    private static readonly (string Words, string Found)[] SeenBefore =
    {
        ("Nog Height",
            "On M21868 (Mainland, Component Nailer V2, September 2026) this was the nog clamp coming "
            + "down very slowly from its upper position (about 200 mm) and being locked in place before "
            + "it reached 45 mm. The fix was the nog clamp's flow control, which needed opening right up. "
            + "Check the flow control before changing ClampDelay or LockDelay.")
    };

    /// <summary>How long after the clamps go down a lock or a check still belongs to them.</summary>
    private static readonly TimeSpan ClampWindow = TimeSpan.FromSeconds(10);

    public static MeasurementFindings Check(
        IReadOnlyList<MachineLogEntry> machineLog, IReadOnlyList<ChangeLogEntry>? changeLog = null)
    {
        var failures = new List<(string What, MeasurementFailure Failure)>();
        var seen = new HashSet<(TimeSpan, string)>();
        var toLock = new List<TimeSpan>();
        TimeSpan? clampDown = null;

        foreach (var entry in machineLog)
        {
            if (entry.Category == MachineLogCategory.OutputChange
                && entry.Description.Contains("Set On", StringComparison.OrdinalIgnoreCase))
            {
                if (IsClampDown(entry.Tag))
                {
                    clampDown = entry.Time;
                }
                else if (IsClampLock(entry.Tag) && clampDown is { } down && entry.Time - down <= ClampWindow)
                {
                    toLock.Add(entry.Time - down);
                    clampDown = null;
                }

                continue;
            }

            var match = Failed.Match(entry.Description);
            if (!match.Success) continue;

            var what = Regex.Replace(match.Groups["what"].Value.Trim(), @"\s+", " ");

            // The same failure is written under two or three tags at the same instant.
            if (!seen.Add((entry.Time, what.ToLowerInvariant()))) continue;

            var since = clampDown is { } d && entry.Time - d <= ClampWindow ? entry.Time - d : (TimeSpan?)null;

            failures.Add((what, new MeasurementFailure(
                entry.Time,
                Number(match.Groups["e"].Value),
                Number(match.Groups["g"].Value),
                since)));

            clampDown = null;
        }

        if (failures.Count == 0) return MeasurementFindings.None;

        var groups = failures
            .GroupBy(f => f.What, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FailedMeasurement(
                g.First().What,
                g.Select(f => f.Failure).ToList(),
                SeenBefore.FirstOrDefault(s => g.Key.Contains(s.Words, StringComparison.OrdinalIgnoreCase)).Found))
            .OrderByDescending(g => g.Count)
            .ToList();

        toLock.Sort();

        var timing = (changeLog ?? Array.Empty<ChangeLogEntry>())
            .Where(c => c.Setting.Contains("Delay", StringComparison.OrdinalIgnoreCase)
                        && (c.Setting.Contains("Clamp", StringComparison.OrdinalIgnoreCase)
                            || c.Setting.Contains("Lock", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.Timestamp)
            .ToList();

        return new MeasurementFindings(
            groups,
            toLock.Count,
            toLock.Count == 0 ? null : toLock[toLock.Count / 2],
            toLock.Count == 0 ? null : toLock[0],
            toLock.Count == 0 ? null : toLock[^1],
            timing);
    }

    /// <summary>
    /// A clamp being sent down: IO-VertFrontClamp, not its lock, its lift or its "Up" return.
    /// The horizontal clamp holds the stud, not the nog, so it is not the one a height check follows.
    /// </summary>
    private static bool IsClampDown(string tag) =>
        tag.Contains("Clamp", StringComparison.OrdinalIgnoreCase)
        && !IsClampLock(tag)
        && !tag.EndsWith("Up", StringComparison.OrdinalIgnoreCase)
        && !tag.Contains("Lift", StringComparison.OrdinalIgnoreCase)
        && !tag.Contains("Horiz", StringComparison.OrdinalIgnoreCase);

    private static bool IsClampLock(string tag) =>
        tag.Contains("ClampLock", StringComparison.OrdinalIgnoreCase);

    private static double Number(string text) =>
        double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
