using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.SpidaLogs;

public class MachineLogFault
{
    public TimeSpan Time { get; init; }
    public string Text { get; init; } = string.Empty;
    public int? StepAtFault { get; init; }
    public IReadOnlyList<MachineLogEntry> Context { get; init; } = Array.Empty<MachineLogEntry>();

    /// <summary>
    /// Somebody pressed something. Worth reporting, never as a fault - and never as evidence of
    /// a sensor or a piece of hardware.
    /// </summary>
    public bool IsOperatorAction { get; init; }

    /// <summary>Fault text with numbers masked, so the same fault matches across attempts.</summary>
    public string Signature => Regex.Replace(Text, @"\d+", "#").Trim().ToUpperInvariant();
}

/// <summary>One unit (panel/board/assembly) the machine attempted.</summary>
public class MachineCycle
{
    public int Number { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public IReadOnlyList<MachineLogFault> Faults { get; init; } = Array.Empty<MachineLogFault>();

    /// <summary>What actually went wrong, with the operator's own button presses taken out.</summary>
    public IReadOnlyList<MachineLogFault> MachineFaults =>
        Faults.Where(f => !f.IsOperatorAction).ToList();

    /// <summary>What the operator did during this attempt.</summary>
    public IReadOnlyList<MachineLogFault> OperatorActions =>
        Faults.Where(f => f.IsOperatorAction).ToList();

    public bool Completed { get; init; }
    public int? HighestStep { get; init; }

    public TimeSpan Duration => End - Start;

    public string Outcome => Completed
        ? $"completed in {Describe(Duration)}"
        : Faults.Count > 0
            ? $"stopped after {Describe(Duration)} - {Faults[^1].Text}"
            : $"no completion logged, ran {Describe(Duration)}";

    internal static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0.#} min" : $"{span.TotalSeconds:0.#} s";
}

public class RepeatedFault
{
    /// <summary>
    /// It landed on a different step each time, so it is not tied to one part of the cycle. Worth
    /// saying: a fault that moves around is a different problem from one that does not.
    /// </summary>
    public bool StepsVary { get; init; }

    public string Text { get; init; } = string.Empty;
    public int Occurrences { get; init; }
    public IReadOnlyList<int> CycleNumbers { get; init; } = Array.Empty<int>();
    public int? StepAtFault { get; init; }
}

public class SpidaLogAnalysis
{
    public string? MachineModelFromLog { get; init; }
    public int MachineLogLines { get; init; }
    public TimeSpan? LogStart { get; init; }
    public TimeSpan? LogEnd { get; init; }

    public IReadOnlyList<MachineCycle> Cycles { get; init; } = Array.Empty<MachineCycle>();
    public MachineCycle? Last { get; init; }
    public MachineCycle? SecondToLast { get; init; }
    public IReadOnlyList<RepeatedFault> RepeatedFaults { get; init; } = Array.Empty<RepeatedFault>();

    public IReadOnlyList<ErrLogEntry> RealErrors { get; init; } = Array.Empty<ErrLogEntry>();
    public IReadOnlyList<ErrLogEntry> CosmeticErrors { get; init; } = Array.Empty<ErrLogEntry>();
    public IReadOnlyList<ErrLogEntry> ErrorsOutsideLogWindow { get; init; } = Array.Empty<ErrLogEntry>();
    public IReadOnlyList<string> RepeatingBackgroundErrors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ChangeLogEntry> RecentSettingChanges { get; init; } = Array.Empty<ChangeLogEntry>();

    /// <summary>
    /// The most recent settings changes whatever their date, newest first. Machines often run for
    /// months on a setting somebody changed once, so "nothing changed this week" is not the same
    /// as "nothing was changed".
    /// </summary>
    public IReadOnlyList<ChangeLogEntry> LatestSettingChanges { get; init; } = Array.Empty<ChangeLogEntry>();

    /// <summary>The session date the changes are measured against, for reporting how long ago.</summary>
    public DateTime SessionDateUtc { get; init; }

    /// <summary>
    /// The tail of the log. A bundle is normally exported within minutes of the problem, so the
    /// last thing the machine did is usually the thing being reported.
    /// </summary>
    public IReadOnlyList<MachineLogEntry> FinalEntries { get; init; } = Array.Empty<MachineLogEntry>();

    /// <summary>
    /// The end of MachineLog.txt exactly as written - every line, every category, nothing
    /// filtered, in file order.
    /// <para>
    /// <see cref="FinalEntries"/> is a summary and it hides things. On a real M22215 export six of
    /// the last twenty lines were InputChange and every one was dropped, including the
    /// LidOpenRequest that caused the servo disable 0.17s later. The report showed the disable
    /// with its cause removed, and nobody reading it could tell why the machine stopped. A
    /// summary is worth having; it is not worth having instead of the file.
    /// </para>
    /// </summary>
    public IReadOnlyList<MachineLogEntry> RawTail { get; init; } = Array.Empty<MachineLogEntry>();

    /// <summary>
    /// What the operator did, counted. Kept apart from faults: an operator pressing stop 29 times
    /// is worth knowing and is not a machine fault.
    /// </summary>
    public IReadOnlyList<RepeatedFault> OperatorActions { get; init; } = Array.Empty<RepeatedFault>();

    /// <summary>What a normal attempt looks like on this machine, in this log.</summary>
    public CycleBaseline? Baseline { get; init; }

    /// <summary>
    /// The last entry that says something about what the machine was doing, ignoring the input
    /// and output chatter that keeps ticking over after it has stopped, and the heartbeat lines
    /// that repeat thousands of times.
    /// </summary>
    public MachineLogEntry? LastNotableEvent { get; init; }

    /// <summary>How many distinct lines were treated as heartbeat and left out of the tail.</summary>
    public int ChatterSkipped { get; init; }

    /// <summary>
    /// How long the log ran on after that last notable event. A long quiet tail means the machine
    /// was sitting there doing nothing when the operator took the file.
    /// </summary>
    public TimeSpan? SilenceBeforeEnd { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public class SpidaLogAnalyserOptions
{
    /// <summary>A completion logged this soon after the start is a false start, not a real unit.</summary>
    public TimeSpan MinimumRealCycle { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Lines either side of a fault to quote, so the fault can be read in context.</summary>
    public int ContextLines { get; set; } = 8;

    /// <summary>Settings changed within this long of the session count as "around the session".</summary>
    public int RecentChangeDays { get; set; } = 1;

    /// <summary>How many of the most recent changes to show whatever their date.</summary>
    public int AlwaysShowLatestChanges { get; set; } = 4;

    /// <summary>An error seen at least this many times is background noise rather than this fault.</summary>
    public int RepeatingErrorThreshold { get; set; } = 5;

    /// <summary>How many lines of the tail of the log to quote.</summary>
    public int FinalEntriesShown { get; set; } = 12;

    /// <summary>
    /// How many lines of the log to print verbatim at the end. Fifty is what a support person
    /// asked for, and it covers the last cycle or two on every machine we have samples from.
    /// </summary>
    public int RawTailLines { get; set; } = 50;
}

/// <summary>
/// Implements the reading method from the Spida log guide: slice MachineLog.txt into the units
/// the machine attempted, pull the fault text out of each, work out which faults repeat, then
/// cross-check ErrLog.txt against Change.log and the MachineLog time window.
/// <para>
/// Deliberately machine-agnostic. Step numbering differs per machine family, so a unit boundary
/// is taken from a step counter resetting rather than from any particular step map.
/// </para>
/// </summary>
public class SpidaLogAnalyser
{
    private readonly SpidaLogAnalyserOptions _options;

    public SpidaLogAnalyser(SpidaLogAnalyserOptions? options = null) => _options = options ?? new SpidaLogAnalyserOptions();

    private static readonly Regex StepPattern = new(@"(?<name>[A-Za-z]*Step)\s*=\s*(?<value>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CompletionPattern = new(
        @"\b(Assembled|Complete[d]?|Ejected|Finished|Unloaded)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lines that appear constantly in every export and are not this fault.</summary>
    private static readonly Regex[] NoisePatterns =
    {
        new(@"\bcomms?\b.*\btimeout\b", RegexOptions.IgnoreCase),
        new(@"\bdriver\b.*\btimeout\b", RegexOptions.IgnoreCase),
        new(@"air (supply )?pressure is low", RegexOptions.IgnoreCase),
        new(@"\b(upload|sync)\b.*\b(task|queue|background)\b", RegexOptions.IgnoreCase)
    };

    /// <summary>
    /// Things the operator did on purpose. These are not faults and must never be reported as
    /// one.
    /// <para>
    /// "Stop All Pressed" turns up 29 times in a real M22215 export and was the headline of that
    /// report's DOES IT REPEAT section, under a sentence saying a repeat points at hardware or a
    /// sensor. It points at a person pressing a button. Sending a technician to meter a sensor
    /// for that is worse than saying nothing.
    /// </para>
    /// </summary>
    private static readonly Regex OperatorAction = new(
        @"\b(stop all pressed|stop pressed|estop pressed|e-stop pressed|reset pressed"
        + @"|start pressed|button pressed|operator (stopped|cancelled|canceled|aborted))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Advisory lines the software writes about how a job is set up. They read like faults
    /// because of one word and they are not events at all.
    /// <para>
    /// "Outfeed clamps not used to prevent jamb" appears 100 times in the same export and matched
    /// only on "jam".
    /// </para>
    /// </summary>
    private static readonly Regex Advisory = new(
        @"\b(not used to prevent|is not set ?up for|will not be used|ignored because)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Drive and axis states that are real faults. These arrive as MotionEvent rather than Other,
    /// which the fault scan used to skip entirely.
    /// <para>
    /// Across the sample logs that hid Servo Movement Error (4, two of them inside a complaint
    /// window nobody was told about), Unsafe to Move Axis (370), Servo Not Setup, Needs to be
    /// Homed and Needs to be Reset. Only Omron F-codes were being picked up, by a different check.
    /// </para>
    /// </summary>
    private static readonly Regex MotionFault = new(
        @"\b(movement error|following error|servo (error|fault|not set ?up)"
        + @"|needs to be (homed|reset)|overtravel|limit (hit|reached))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Descriptive Other-category text that reads like a fault rather than a counter.</summary>
    private static readonly Regex FaultWording = new(
        @"\b(cannot|can't|unable|not set ?up|failed|failure|fault|error|stopped|stop\b|lost|jam|check|press|estop|e-stop|revert|retry|try again|timeout|missing|invalid"
        // The Tornado rejects a board with "Board not expected length" and "Board not expected
        // size", which carry none of the words above, so its real faults were being read as
        // ordinary chatter and dropped.
        + @"|not expected|unexpected|out of tolerance|reject)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SpidaLogAnalysis Analyse(
        IReadOnlyList<MachineLogEntry> machineLog,
        IReadOnlyList<ErrLogEntry> errLog,
        IReadOnlyList<ChangeLogEntry> changeLog,
        DateTime sessionDateUtc)
    {
        var notes = new List<string>();

        if (machineLog.Count == 0)
        {
            notes.Add("MachineLog.txt was empty or could not be read, so no machine behaviour could be examined.");
        }

        var cycles = DropTheMachinesOwnHabits(SliceIntoCycles(machineLog), notes);
        var repeated = FindRepeatedFaults(cycles);

        var logStart = machineLog.Count > 0 ? machineLog[0].Time : (TimeSpan?)null;
        var logEnd = machineLog.Count > 0 ? machineLog[^1].Time : (TimeSpan?)null;

        var (real, cosmetic, outside, backgroundNoise) = ClassifyErrors(errLog, changeLog, logStart, logEnd, sessionDateUtc, notes);

        var chatter = FindChatter(machineLog);

        var notable = machineLog
            .Where(e => e.Category is MachineLogCategory.Other or MachineLogCategory.MotionEvent)
            .Where(e => !chatter.Contains($"{e.Tag}|{e.Description}"))
            .Where(e => !Housekeeping.IsMatch($"{e.Tag} {e.Description}"))
            .ToList();

        var lastNotable = notable.LastOrDefault();

        return new SpidaLogAnalysis
        {
            MachineModelFromLog = MachineLogFile.FindMachineModel(machineLog),
            MachineLogLines = machineLog.Count,
            LogStart = logStart,
            LogEnd = logEnd,
            Cycles = cycles,
            Last = cycles.Count > 0 ? cycles[^1] : null,
            SecondToLast = cycles.Count > 1 ? cycles[^2] : null,
            RepeatedFaults = repeated,
            OperatorActions = FindRepeatedActions(cycles),
            Baseline = CycleBaseline.From(cycles),
            RealErrors = real,
            CosmeticErrors = cosmetic,
            ErrorsOutsideLogWindow = outside,
            RepeatingBackgroundErrors = backgroundNoise,
            RecentSettingChanges = RecentChanges(changeLog, sessionDateUtc),
            FinalEntries = notable.TakeLast(_options.FinalEntriesShown).ToList(),
            RawTail = machineLog.TakeLast(_options.RawTailLines).ToList(),
            ChatterSkipped = chatter.Count,
            LastNotableEvent = lastNotable,
            SilenceBeforeEnd = lastNotable is not null && logEnd is { } end
                ? end - lastNotable.Time
                : null,
            LatestSettingChanges = changeLog
                .OrderByDescending(c => c.Timestamp)
                .Take(_options.AlwaysShowLatestChanges)
                .ToList(),
            SessionDateUtc = sessionDateUtc,
            Notes = notes
        };
    }

    /// <summary>
    /// Cuts the log where a step counter jumps back down, which marks a new unit on every machine
    /// family even where the step numbers themselves mean different things.
    /// </summary>
    private List<MachineCycle> SliceIntoCycles(IReadOnlyList<MachineLogEntry> entries)
    {
        var boundaries = new List<int>();
        var previousStep = int.MinValue;

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Category != MachineLogCategory.Other) continue;

            var step = ReadStep(entries[i]);
            if (step is null) continue;

            if (previousStep != int.MinValue && step < previousStep)
            {
                boundaries.Add(i);
            }

            previousStep = step.Value;
        }

        if (entries.Count == 0) return new List<MachineCycle>();
        if (boundaries.Count == 0) boundaries.Add(0);
        if (boundaries[0] != 0) boundaries.Insert(0, 0);

        var cycles = new List<MachineCycle>();
        for (var b = 0; b < boundaries.Count; b++)
        {
            var from = boundaries[b];
            var to = b + 1 < boundaries.Count ? boundaries[b + 1] : entries.Count;
            var slice = entries.Skip(from).Take(to - from).ToList();
            if (slice.Count == 0) continue;

            var duration = slice[^1].Time - slice[0].Time;
            var completed = slice.Any(e => CompletionPattern.IsMatch(e.Description))
                            && duration >= _options.MinimumRealCycle;

            cycles.Add(new MachineCycle
            {
                Number = cycles.Count + 1,   // renumbered below once slivers are dropped
                Start = slice[0].Time,
                End = slice[^1].Time,
                Completed = completed,
                HighestStep = slice.Select(ReadStep).Where(s => s is not null).Select(s => s!.Value).DefaultIfEmpty().Max(),
                Faults = FindFaults(entries, from, to)
            });
        }

        // A trailing step drop can leave a slice of a fraction of a second with nothing in it.
        // That is the tail of the log, not an attempt, so drop it rather than report it as one.
        var real = cycles
            .Where(c => c.Duration >= _options.MinimumRealCycle || c.Faults.Count > 0 || c.Completed)
            .ToList();

        if (real.Count == 0) real = cycles;

        return real
            .Select((c, index) => new MachineCycle
            {
                Number = index + 1,
                Start = c.Start,
                End = c.End,
                Completed = c.Completed,
                HighestStep = c.HighestStep,
                Faults = c.Faults
            })
            .ToList();
    }

    private List<MachineLogFault> FindFaults(IReadOnlyList<MachineLogEntry> all, int from, int to)
    {
        var faults = new List<MachineLogFault>();
        var currentStep = (int?)null;

        for (var i = from; i < to; i++)
        {
            var entry = all[i];

            // MotionEvent carries the drive-level faults, and skipping the whole category hid
            // every one of them. Input and output changes are states, not faults, and stay out.
            if (entry.Category is not (MachineLogCategory.Other or MachineLogCategory.MotionEvent))
                continue;

            var step = ReadStep(entry);
            if (step is not null)
            {
                currentStep = step;
                continue;   // a step counter is not a fault
            }

            var text = entry.Description.Trim();
            if (text.Length < 8) continue;

            var motion = entry.Category == MachineLogCategory.MotionEvent;

            // An axis reporting OK, Moving or Disabled is a state. Only the real failures count.
            if (motion ? !MotionFault.IsMatch(text) : !FaultWording.IsMatch(text)) continue;
            if (NoisePatterns.Any(p => p.IsMatch(text))) continue;
            if (Advisory.IsMatch(text)) continue;

            faults.Add(new MachineLogFault
            {
                Time = entry.Time,
                Text = motion ? $"{entry.Tag}: {text}" : text,
                StepAtFault = currentStep,
                IsOperatorAction = OperatorAction.IsMatch(text),
                Context = all
                    .Skip(Math.Max(0, i - _options.ContextLines))
                    .Take(_options.ContextLines * 2 + 1)
                    .ToList()
            });
        }

        return faults;
    }

    /// <summary>
    /// Anything landing in more than half the attempts is what this machine does, not what went
    /// wrong with it.
    /// <para>
    /// "Unsafe to Move Axis" fires 370 times in a real M22215 export - every time the blade moves
    /// during a cut - and treating it as a fault buried the four Servo Movement Errors that
    /// actually mattered under a wall of noise. The rule is deliberately about frequency rather
    /// than a list of words: the next machine will have its own habits and nobody will remember
    /// to add them.
    /// </para>
    /// <para>
    /// It is said out loud in the report's notes rather than done quietly, because a fault in
    /// every cycle could also be a machine that is broken in every cycle.
    /// </para>
    /// </summary>
    private static List<MachineCycle> DropTheMachinesOwnHabits(List<MachineCycle> cycles, List<string> notes)
    {
        if (cycles.Count < 4) return cycles;

        var attemptsWith = cycles
            .SelectMany(c => c.Faults.Where(f => !f.IsOperatorAction)
                .Select(f => f.Signature).Distinct()
                .Select(signature => (signature, c.Number)))
            .GroupBy(pair => pair.signature)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Number).Distinct().Count(), StringComparer.Ordinal);

        var habits = attemptsWith
            .Where(pair => pair.Value > cycles.Count / 2)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (habits.Count == 0) return cycles;

        var dropped = cycles
            .SelectMany(c => c.Faults)
            .Where(f => habits.Contains(f.Signature))
            .GroupBy(f => f.Text)
            .Select(g => $"\"{g.Key}\" (x{g.Count()})")
            .ToList();

        notes.Add($"Left out of the fault list because {(dropped.Count == 1 ? "it appears" : "they appear")} "
                  + $"in more than half of this machine's attempts, which makes "
                  + $"{(dropped.Count == 1 ? "it" : "them")} this machine's normal behaviour rather than a "
                  + $"fault: {string.Join(", ", dropped)}. Say so if that is wrong.");

        return cycles
            .Select(c => new MachineCycle
            {
                Number = c.Number,
                Start = c.Start,
                End = c.End,
                Completed = c.Completed,
                HighestStep = c.HighestStep,
                Faults = c.Faults.Where(f => !habits.Contains(f.Signature)).ToList()
            })
            .ToList();
    }

    /// <summary>Operator actions, counted the same way faults are, and kept separate from them.</summary>
    private static List<RepeatedFault> FindRepeatedActions(List<MachineCycle> cycles) => cycles
        .SelectMany(cycle => cycle.Faults.Where(f => f.IsOperatorAction).Select(fault => (cycle, fault)))
        .GroupBy(pair => pair.fault.Signature)
        .Select(group => new RepeatedFault
        {
            Text = group.First().fault.Text,
            Occurrences = group.Count(),
            CycleNumbers = group.Select(p => p.cycle.Number).Distinct().OrderBy(n => n).ToList(),
            // Only claim a step when every occurrence really did land on the same one. Printing
            // "at step 5" while the report's own attempt list says step 12 is how a reader stops
            // trusting the whole thing.
            StepAtFault = group.Select(p => p.fault.StepAtFault).Distinct().Count() == 1
                ? group.First().fault.StepAtFault
                : null,
            StepsVary = group.Select(p => p.fault.StepAtFault).Distinct().Count() > 1
        })
        .OrderByDescending(a => a.Occurrences)
        .ToList();

    /// <summary>
    /// A fault at the same step across two or more attempts is a real hardware or sensor problem;
    /// one that appears once is more likely a transient.
    /// </summary>
    private static List<RepeatedFault> FindRepeatedFaults(List<MachineCycle> cycles)
    {
        return cycles
            .SelectMany(cycle => cycle.Faults.Where(f => !f.IsOperatorAction).Select(fault => (cycle, fault)))
            .GroupBy(pair => pair.fault.Signature)
            .Where(group => group.Select(p => p.cycle.Number).Distinct().Count() >= 2)
            .Select(group => new RepeatedFault
            {
                Text = group.First().fault.Text,
                Occurrences = group.Count(),
                CycleNumbers = group.Select(p => p.cycle.Number).Distinct().OrderBy(n => n).ToList(),
                // Only claim a step when every occurrence really did land on the same one. Printing
                // "at step 5" while the report's own attempt list says step 12 is how a reader stops
                // trusting the whole thing.
                StepAtFault = group.Select(p => p.fault.StepAtFault).Distinct().Count() == 1
                    ? group.First().fault.StepAtFault
                    : null,
                StepsVary = group.Select(p => p.fault.StepAtFault).Distinct().Count() > 1
            })
            .OrderByDescending(f => f.Occurrences)
            .ToList();
    }

    private (List<ErrLogEntry> Real, List<ErrLogEntry> Cosmetic, List<ErrLogEntry> Outside, List<string> Background)
        ClassifyErrors(
            IReadOnlyList<ErrLogEntry> errors,
            IReadOnlyList<ChangeLogEntry> changes,
            TimeSpan? logStart,
            TimeSpan? logEnd,
            DateTime sessionDateUtc,
            List<string> notes)
    {
        var real = new List<ErrLogEntry>();
        var cosmetic = new List<ErrLogEntry>();
        var outside = new List<ErrLogEntry>();

        // An error repeating this often fires every session and is not this complaint.
        var background = errors
            .GroupBy(e => e.Signature)
            .Where(g => g.Count() >= _options.RepeatingErrorThreshold)
            .Select(g => $"{g.First().Text} (x{g.Count()})")
            .ToList();

        var backgroundSignatures = errors
            .GroupBy(e => e.Signature)
            .Where(g => g.Count() >= _options.RepeatingErrorThreshold)
            .Select(g => g.Key)
            .ToHashSet();

        var changeTimes = changes.Select(c => Truncate(c.Timestamp)).ToHashSet();

        if (logStart is null || logEnd is null)
        {
            notes.Add("MachineLog.txt has no timestamps, so errors could not be placed against machine behaviour.");
        }
        else
        {
            notes.Add($"MachineLog.txt covers {logStart:hh\\:mm\\:ss} to {logEnd:hh\\:mm\\:ss}; "
                      + $"errors are matched against that window on {sessionDateUtc.ToLocalTime():yyyy-MM-dd}, "
                      + "which is taken from the file name rather than the log itself.");
        }

        foreach (var error in errors)
        {
            if (backgroundSignatures.Contains(error.Signature)) continue;

            // A settings save that really happened makes the UI error that follows it cosmetic.
            if (changeTimes.Contains(Truncate(error.Timestamp)))
            {
                cosmetic.Add(error);
                continue;
            }

            if (logStart is { } start && logEnd is { } end)
            {
                var timeOfDay = error.Timestamp.TimeOfDay;
                var sameDay = error.Timestamp.Date == sessionDateUtc.ToLocalTime().Date;

                if (!sameDay || timeOfDay < start || timeOfDay > end)
                {
                    outside.Add(error);
                    continue;
                }
            }

            real.Add(error);
        }

        return (real, cosmetic, outside, background);
    }

    private List<ChangeLogEntry> RecentChanges(IReadOnlyList<ChangeLogEntry> changes, DateTime sessionDateUtc)
    {
        var sessionLocal = sessionDateUtc.ToLocalTime();
        var from = sessionLocal.Date.AddDays(-_options.RecentChangeDays);

        return changes
            .Where(c => c.Timestamp >= from && c.Timestamp <= sessionLocal.Date.AddDays(1))
            .OrderBy(c => c.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Reporting and upload lines. They are written by the export itself rather than by the
    /// machine, so the last one is always the moment the file was taken - never the problem.
    /// </summary>
    private static readonly Regex Housekeeping = new(
        @"\b(sharepoint|cloudreport|upload|telemetry)\w*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Lines that repeat so often they are a heartbeat rather than an event. On a real Tornado
    /// export "Other, CIP, a" is 23,550 of 100,000 lines - nearly a quarter of the file - and
    /// taking it as the machine's last act points the whole report at nothing.
    /// </summary>
    private static HashSet<string> FindChatter(IReadOnlyList<MachineLogEntry> entries)
    {
        var threshold = Math.Max(25, entries.Count / 50);

        return entries
            .GroupBy(e => $"{e.Tag}|{e.Description}")
            .Where(g => g.Count() > threshold)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static int? ReadStep(MachineLogEntry entry)
    {
        var match = StepPattern.Match($"{entry.Tag} {entry.Description}");
        return match.Success && int.TryParse(match.Groups["value"].Value, out var value) ? value : null;
    }

    private static DateTime Truncate(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
}
