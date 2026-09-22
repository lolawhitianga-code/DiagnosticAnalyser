using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One homing of the two side pullers, and how far apart they finished.</summary>
public record HomingRun(TimeSpan StartedAt, double FixedSeconds, double FloatingSeconds)
{
    /// <summary>Positive when the fixed side finished last.</summary>
    public double Gap => FixedSeconds - FloatingSeconds;

    /// <summary>
    /// A full home from wherever the axes were, as opposed to a re-home from nearly home. Only a
    /// full one travels far enough for a late sensor to show.
    /// </summary>
    public bool Full => Math.Max(FixedSeconds, FloatingSeconds) > HomingBalanceCheck.FullRunSeconds;

    public bool Unbalanced => Full && Math.Abs(Gap) >= HomingBalanceCheck.GapWorthRaising;

    public string LateSide => Gap > 0 ? "fixed" : "floating";
}

public record HomingFindings(IReadOnlyList<HomingRun> Runs)
{
    public IReadOnlyList<HomingRun> Unbalanced => Runs.Where(r => r.Unbalanced).ToList();

    public bool Any => Unbalanced.Count > 0;
}

/// <summary>
/// Compares how long the two side pullers take to home.
/// <para>
/// On a Raked Wall Extruder V3 both trolleys home to a sensor (HomeMode = Sensor), and the sensor
/// <b>defines</b> the axis zero: when it trips, the axis is told it is at HomePosition. So a sensor
/// that has moved moves the machine's idea of every position after it - with nothing in the change
/// log, because nothing was changed. M21856 at Mainland came in with the fixed side gripper out by
/// 30 mm, and the fixed side home sensor was sitting too far from its aluminium target block.
/// </para>
/// <para>
/// How home is found, per Spida: the trolley drives onto its aluminium block until the sensor sees
/// it, then drives slowly off it, and the exact moment the sensor turns off is home. That falling
/// edge is the accurate spot, which is why the sensor has to sit 1-2 mm from the block - too far
/// away and the edge is no longer crisp, so home lands in the wrong place and that side takes
/// longer to find it. On every healthy full homing run in the logs here - M21737, M21844, and four
/// M20771 exports - the two pullers report OK <b>in the same millisecond</b>. On M21856 the fixed
/// side finished 2.17 s after the floating side.
/// </para>
/// <para>
/// Short re-homes from nearly home wobble by up to 0.8 s and mean nothing, so only a full run is
/// judged. The time is not converted to millimetres: that needs the home velocity's units and the
/// homing back-off, and a wrong conversion would be worse than a plain "this side was late".
/// </para>
/// </summary>
public static class HomingBalanceCheck
{
    /// <summary>A homing that took longer than this came from well away from home.</summary>
    internal const double FullRunSeconds = 10;

    /// <summary>Healthy full runs finish together to the millisecond; M21856 was 2.17 s apart.</summary>
    internal const double GapWorthRaising = 1.0;

    private static readonly Regex Status = new(
        @"^Node(?<node>[01]) Status$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static HomingFindings Check(IReadOnlyList<MachineLogEntry> entries)
    {
        var runs = new List<HomingRun>();
        TimeSpan? start = null;
        double? fixedDone = null, floatingDone = null;

        foreach (var entry in entries)
        {
            if (entry.Category != MachineLogCategory.MotionEvent) continue;

            var match = Status.Match(entry.Tag.Trim());
            if (!match.Success) continue;

            var node = match.Groups["node"].Value;
            var state = entry.Description.Trim();

            if (node == "0" && state.Equals("Homing", StringComparison.OrdinalIgnoreCase))
            {
                start = entry.Time;
                fixedDone = floatingDone = null;
                continue;
            }

            if (start is null || !state.Equals("OK", StringComparison.OrdinalIgnoreCase)) continue;

            var took = (entry.Time - start.Value).TotalSeconds;
            if (node == "0" && fixedDone is null) fixedDone = took;
            if (node == "1" && floatingDone is null) floatingDone = took;

            if (fixedDone is { } f && floatingDone is { } l)
            {
                runs.Add(new HomingRun(start.Value, f, l));
                start = null;
            }
        }

        return new HomingFindings(runs);
    }
}
