using System.Globalization;

namespace DiagFileMonitor.Core.Fleet;

/// <summary>One board the saw was asked for, and what came of it.</summary>
public record BoardRecord(DateTime CompletedAt, int Members, double LengthMetres, int Cuts);

/// <summary>A stretch the machine was powered and running.</summary>
public record RunSpan(DateTime From, DateTime To)
{
    public TimeSpan Length => To - From;
}

public class SawProductionSummary
{
    public string SerialNumber { get; init; } = string.Empty;

    public DateTime? From { get; init; }
    public DateTime? To { get; init; }

    public IReadOnlyList<BoardRecord> Boards { get; init; } = Array.Empty<BoardRecord>();

    /// <summary>Individual member cuts, which is what the blade actually did.</summary>
    public int MembersCut { get; init; }

    public IReadOnlyList<RunSpan> Runs { get; init; } = Array.Empty<RunSpan>();

    /// <summary>People seen logging in. Tells you how many operators a site really has on it.</summary>
    public IReadOnlyList<string> Operators { get; init; } = Array.Empty<string>();

    /// <summary>Lines whose wording this build did not recognise. Counted, never dropped silently.</summary>
    public int Unreadable { get; init; }

    /// <summary>Events that arrived twice in a row, which this format does constantly.</summary>
    public int DuplicatesSkipped { get; init; }

    public int BoardsCompleted => Boards.Count;
    public double LinealMetres => Boards.Sum(b => b.LengthMetres);
    public int Cuts => Boards.Sum(b => b.Cuts);

    /// <summary>
    /// Time between each MachineStarted and its MachineStopped, added up.
    /// <para>
    /// NOT machine powered hours, and it must never be labelled as such. On the M22215 sample
    /// these pair 307 times across four days with a <b>median span of 25 seconds</b> and a longest
    /// of nine minutes - that is the saw motor running for a cut, not the machine being switched
    /// on. It is a good wear proxy for the blade and the motor and nothing else.
    /// </para>
    /// <para>
    /// The starts outnumber the stops (555 to 322 on that sample), so some spans never close and
    /// are dropped rather than left open across a weekend.
    /// </para>
    /// </summary>
    public TimeSpan MotorRunTime => Runs.Aggregate(TimeSpan.Zero, (total, run) => total + run.Length);

    public IReadOnlyList<DateOnly> Days => Boards.Select(b => DateOnly.FromDateTime(b.CompletedAt))
        .Distinct().OrderBy(d => d).ToList();

    /// <summary>
    /// First board to last board on each day the machine produced anything.
    /// <para>
    /// This is the honest denominator for a saw. The log has no roster in it and no power-on
    /// event, so the span the machine was actually working is the only measured thing available,
    /// and it cannot be stretched by a shift model nobody has confirmed.
    /// </para>
    /// </summary>
    public IReadOnlyList<(DateOnly Day, int Boards, TimeSpan Span)> WorkingDays => Boards
        .GroupBy(b => DateOnly.FromDateTime(b.CompletedAt))
        .OrderBy(g => g.Key)
        .Select(g => (g.Key, g.Count(), g.Max(b => b.CompletedAt) - g.Min(b => b.CompletedAt)))
        .ToList();

    public TimeSpan WorkingSpan => WorkingDays.Aggregate(TimeSpan.Zero, (t, d) => t + d.Span);

    /// <summary>Boards an hour across the span the machine was actually working.</summary>
    public double BoardsPerWorkingHour => WorkingSpan > TimeSpan.Zero
        ? BoardsCompleted / WorkingSpan.TotalHours
        : 0;

    /// <summary>
    /// The best day this machine has shown it can do, as boards an hour. What the machine is
    /// capable of on this site, proven by the site's own numbers rather than by a brochure.
    /// </summary>
    public double BestDayRate => WorkingDays
        .Where(d => d.Boards >= 20 && d.Span > TimeSpan.FromMinutes(30))
        .Select(d => d.Boards / d.Span.TotalHours)
        .DefaultIfEmpty(0)
        .Max();

    public bool Any => Boards.Count > 0 || Runs.Count > 0;
}

/// <summary>
/// Reads the saw's own production report - <c>Reports/LatestReport.txt</c> on a SprintM600 or a
/// Tornado.
/// <para>
/// The app could already read an extruder's panel log and could read nothing at all from a saw, so
/// half the installed base produced no measurable output. The formats are different: an extruder
/// logs panels, a saw logs boards and individual member cuts, and only the saw logs when it was
/// started and stopped.
/// </para>
/// <para>
/// Events arrive in consecutive duplicate pairs throughout this format - every MachineStarted in
/// the sample file is written twice - so a repeat of the same event at the same second is dropped
/// and counted rather than doubling every figure.
/// </para>
/// </summary>
public static class SawProduction
{
    /// <summary>A gap longer than this is the machine being off overnight, not a run.</summary>
    private static readonly TimeSpan LongestPlausibleRun = TimeSpan.FromHours(16);

    public static SawProductionSummary Read(IEnumerable<string> lines, string serialNumber = "")
    {
        var boards = new List<BoardRecord>();
        var runs = new List<RunSpan>();
        var operators = new List<string>();

        var members = 0;
        var unreadable = 0;
        var duplicates = 0;

        DateTime? runningSince = null;
        var previous = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            // The format writes most events twice. The second one is not a second board.
            if (line == previous)
            {
                duplicates++;
                continue;
            }

            previous = line;

            var parts = line.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length < 2) { unreadable++; continue; }

            if (ReadTime(parts[1]) is not { } at)
            {
                // UserLogin carries a name rather than only a time, and is still worth having.
                unreadable++;
                continue;
            }

            switch (parts[0].ToUpperInvariant())
            {
                case "MACHINESTARTED":
                    runningSince ??= at;
                    break;

                case "MACHINESTOPPED":
                    if (runningSince is { } from)
                    {
                        var span = at - from;

                        // A stop that never came leaves a span covering a weekend. That is the
                        // machine being switched off, not sixty hours of running.
                        if (span > TimeSpan.Zero && span <= LongestPlausibleRun)
                            runs.Add(new RunSpan(from, at));

                        runningSince = null;
                    }
                    break;

                case "BOARDCOMPLETED":
                    boards.Add(ReadBoard(at, parts));
                    break;

                case "MEMBERCUT":
                    members++;
                    break;

                case "USERLOGIN":
                    if (parts.Length > 2 && parts[2].Length > 0) operators.Add(parts[2]);
                    break;

                case "BOARDSTARTED":
                    break;   // the completion is what counts; a start on its own is an intention

                default:
                    unreadable++;
                    break;
            }
        }

        return new SawProductionSummary
        {
            SerialNumber = serialNumber,
            From = boards.Count > 0 ? boards.Min(b => b.CompletedAt) : runs.FirstOrDefault()?.From,
            To = boards.Count > 0 ? boards.Max(b => b.CompletedAt) : runs.LastOrDefault()?.To,
            Boards = boards,
            MembersCut = members,
            Runs = runs,
            Operators = operators.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o).ToList(),
            Unreadable = unreadable,
            DuplicatesSkipped = duplicates
        };
    }

    public static SawProductionSummary ReadFile(string path, string serialNumber = "") =>
        File.Exists(path)
            ? Read(File.ReadLines(path), serialNumber)
            : new SawProductionSummary { SerialNumber = serialNumber };

    /// <summary>
    /// <c>BoardCompleted, 20260914 07:16:49, 4, 3584.3, 3.584, 5, name, ?, len, ...</c>
    /// <para>
    /// Member count, length in millimetres and length in metres are the three that agree with each
    /// other across the sample file, so those are read. The sixth field looks like a cut count and
    /// is taken as one; the trailing triples are per-member and are not read, because what the
    /// middle field of each triple means has not been confirmed with anybody.
    /// </para>
    /// </summary>
    private static BoardRecord ReadBoard(DateTime at, string[] parts)
    {
        var members = Number(parts, 2);
        var metres = Number(parts, 4);

        // Where the metres field is missing, fall back to the millimetre field beside it rather
        // than reporting a board with no length.
        if (metres <= 0 && Number(parts, 3) is var mm && mm > 0) metres = mm / 1000;

        return new BoardRecord(at, (int)members, metres, (int)Number(parts, 5));
    }

    private static double Number(string[] parts, int index) =>
        index < parts.Length && double.TryParse(parts[index], NumberStyles.Any,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    /// <summary>Timestamps are written <c>yyyyMMdd HH:mm:ss</c>.</summary>
    private static DateTime? ReadTime(string value) =>
        DateTime.TryParseExact(value, "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
