using System.Text.RegularExpressions;

namespace DiagFileMonitor.Core.SpidaLogs;

public enum SignalKind
{
    Input,
    Output
}

/// <summary>
/// One I/O point, as the log identifies it.
/// <para>
/// All three parts are needed. Neither half is unique on a real machine: on M21737 the name
/// <c>LowerGunFire</c> is two coils, COM7-6.5 and COM7-6.2, one for each side of the machine; on
/// the M20716 saw the address <c>192.168.250.1-0.2</c> is input <c>FollowerUp</c> and output
/// <c>IO-DeckRev</c>, and <c>192.168.250.1-1.12</c> carries two outputs,
/// <c>IO-OutfeedDriveTopClamp2Down</c> and <c>IO-OutfeedDriveTopClamp3Down</c>, which are logged
/// separately and genuinely move apart. Keying on either half alone would merge two points or
/// split one.
/// </para>
/// </summary>
public readonly record struct SignalId(SignalKind Kind, string Name, string Address)
{
    public string Display => Address.Length > 0 ? $"{Name} ({Address})" : Name;

    public override string ToString() => $"{Kind} {Display}";
}

/// <summary>Where a state came from, because an assumed state and a measured one are not the same.</summary>
public enum StateSource
{
    /// <summary>The log says it changed to this, at or before the moment asked about.</summary>
    Measured,

    /// <summary>
    /// Nothing had changed it yet at that moment, so the state is read backwards from the next
    /// change: a point that is later switched on was off before, and one later switched off was
    /// on. Sound because these lines are changes - across 20,747 of them on the sample logs, no
    /// point ever logs the same value twice running.
    /// </summary>
    ReadBackFromNextChange
}

public record SignalChange(SignalId Id, TimeSpan Time, bool On, int LineNumber, int Day)
{
    /// <summary>Time since the first line of the log, so a log that runs past midnight still sorts.</summary>
    public TimeSpan Elapsed => Time + TimeSpan.FromDays(Day);
}

public record SignalState(
    SignalId Id,
    bool On,
    StateSource Source,
    TimeSpan? Since,
    int? SinceLine,
    int ChangesBefore,
    int ChangesTotal)
{
    public string OnOff => On ? "On" : "Off";

    public string Held => Since is { } since
        ? $"since {since:hh\\:mm\\:ss\\.fff}"
        : "not yet touched in this log";
}

/// <summary>Every point's state at one moment, split the way a person reads a machine.</summary>
public record IoSnapshot(
    TimeSpan Moment,
    int LineNumber,
    string LineText,
    IReadOnlyList<SignalState> Outputs,
    IReadOnlyList<SignalState> Inputs)
{
    public int OutputsOn => Outputs.Count(s => s.On);
    public int InputsOn => Inputs.Count(s => s.On);

    /// <summary>Points nothing had touched yet, so their state is read backwards rather than measured.</summary>
    public int ReadBackwards => Outputs.Concat(Inputs).Count(s => s.Source != StateSource.Measured);

    public string Summary =>
        $"{OutputsOn} of {Outputs.Count} output(s) on, {InputsOn} of {Inputs.Count} input(s) on"
        + (ReadBackwards > 0 ? $". {ReadBackwards} read backwards from a later change." : ".");
}

/// <summary>
/// Replays the InputChange and OutputChange lines of a MachineLog so the state of every point can
/// be asked for at any moment in the file.
/// <para>
/// Replay is by line order, not by clock. The clock in these logs is not always monotonic - it
/// jitters by up to a second, and a long log runs past midnight - but the file is written in the
/// order things happened, so line order is the one thing that can be trusted.
/// </para>
/// </summary>
public class IoTimeline
{
    // Two shapes appear across every sample log: "Output (ADDR) Set On|Off" and
    // "Input (ADDR) Changed to 1|0". The rest of the pattern is slack in case an older or newer
    // build words it differently; anything that still does not match is counted, not dropped.
    private static readonly Regex ChangePattern = new(
        @"^(?<kind>Input|Output)\s*\((?<addr>[^)]*)\)\s*(?:Set|Changed\s+to|=)?\s*(?<value>On|Off|True|False|1|0)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A clock that jumps back further than this is a new day, not jitter.</summary>
    private static readonly TimeSpan RolloverGap = TimeSpan.FromHours(12);

    private readonly IReadOnlyList<MachineLogEntry> _entries;
    private readonly List<SignalChange> _changes = new();
    private readonly Dictionary<SignalId, List<SignalChange>> _bySignal = new();

    private IoTimeline(IReadOnlyList<MachineLogEntry> entries) => _entries = entries;

    public IReadOnlyList<MachineLogEntry> Entries => _entries;
    public IReadOnlyList<SignalChange> Changes => _changes;

    /// <summary>Every point this log ever moves, outputs first, each side in name order.</summary>
    public IReadOnlyList<SignalId> Signals { get; private set; } = Array.Empty<SignalId>();

    /// <summary>InputChange or OutputChange lines whose wording could not be read.</summary>
    public IReadOnlyList<MachineLogEntry> Unreadable { get; private set; } = Array.Empty<MachineLogEntry>();

    /// <summary>How many midnights the log runs through.</summary>
    public int Days { get; private set; }

    public TimeSpan FirstTime => _entries.Count > 0 ? _entries[0].Time : TimeSpan.Zero;
    public TimeSpan LastTime => _entries.Count > 0 ? _entries[^1].Time : TimeSpan.Zero;

    public static IoTimeline Build(IReadOnlyList<MachineLogEntry> entries)
    {
        var timeline = new IoTimeline(entries);
        timeline.Replay();
        return timeline;
    }

    public static IoTimeline FromFile(string path) => Build(MachineLogFile.ParseFile(path));

    private void Replay()
    {
        var unreadable = new List<MachineLogEntry>();
        var day = 0;
        TimeSpan? previous = null;

        foreach (var entry in _entries)
        {
            if (previous is { } last && last - entry.Time > RolloverGap) day++;
            previous = entry.Time;

            if (entry.Category is not (MachineLogCategory.InputChange or MachineLogCategory.OutputChange))
                continue;

            var match = ChangePattern.Match(entry.Description.Trim());
            if (!match.Success)
            {
                unreadable.Add(entry);
                continue;
            }

            var kind = match.Groups["kind"].Value.Equals("Input", StringComparison.OrdinalIgnoreCase)
                ? SignalKind.Input
                : SignalKind.Output;

            var value = match.Groups["value"].Value;
            var on = value is "1" || value.Equals("On", StringComparison.OrdinalIgnoreCase)
                     || value.Equals("True", StringComparison.OrdinalIgnoreCase);

            var change = new SignalChange(
                new SignalId(kind, entry.Tag, match.Groups["addr"].Value.Trim()),
                entry.Time, on, entry.LineNumber, day);

            _changes.Add(change);

            if (!_bySignal.TryGetValue(change.Id, out var list))
                _bySignal[change.Id] = list = new List<SignalChange>();

            list.Add(change);
        }

        Days = day;
        Unreadable = unreadable;
        Signals = _bySignal.Keys
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The state of every point as at that line, the line itself included - so clicking
    /// "TopStudClamp Set On" shows it on, which is what anybody reading the log expects.
    /// </summary>
    public IoSnapshot AtLine(int lineNumber)
    {
        var outputs = new List<SignalState>();
        var inputs = new List<SignalState>();

        foreach (var id in Signals)
        {
            var state = StateOf(id, lineNumber);
            (id.Kind == SignalKind.Output ? outputs : inputs).Add(state);
        }

        var entry = _entries.LastOrDefault(e => e.LineNumber <= lineNumber) ?? _entries.FirstOrDefault();

        return new IoSnapshot(
            entry?.Time ?? TimeSpan.Zero,
            entry?.LineNumber ?? 0,
            entry?.Display ?? string.Empty,
            outputs, inputs);
    }

    /// <summary>
    /// The state at a clock time. Where a log runs past midnight the same clock time happens
    /// twice, and this takes the later one - use <see cref="AtLine"/> when that matters.
    /// </summary>
    public IoSnapshot AtTime(TimeSpan clock)
    {
        var line = LineAt(clock);
        return AtLine(line);
    }

    /// <summary>The last line at or before that clock time, or the first line if it is earlier than all of them.</summary>
    public int LineAt(TimeSpan clock)
    {
        var line = 0;

        foreach (var entry in _entries)
            if (entry.Time <= clock) line = entry.LineNumber;

        return line > 0 ? line : _entries.FirstOrDefault()?.LineNumber ?? 0;
    }

    private SignalState StateOf(SignalId id, int lineNumber)
    {
        var history = _bySignal[id];

        SignalChange? before = null;
        var countBefore = 0;

        foreach (var change in history)
        {
            if (change.LineNumber > lineNumber) break;
            before = change;
            countBefore++;
        }

        if (before is { } known)
            return new SignalState(id, known.On, StateSource.Measured, known.Time, known.LineNumber,
                countBefore, history.Count);

        // Nothing has touched it yet. The next change tells us what it was before: a point that
        // is later switched on was off until then, and one later switched off was on.
        var next = history[0];

        return new SignalState(id, !next.On, StateSource.ReadBackFromNextChange, null, null,
            0, history.Count);
    }

    /// <summary>
    /// Points that report the same address. Worth surfacing rather than hiding: a technician
    /// tracing a wire to <c>192.168.250.1-1.12</c> needs to know two named outputs sit on it.
    /// </summary>
    public IReadOnlyList<IGrouping<string, SignalId>> SharedAddresses => Signals
        .Where(s => s.Address.Length > 0)
        .GroupBy(s => s.Address, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Every change to one point, for the "when did this last move" question.</summary>
    public IReadOnlyList<SignalChange> HistoryOf(SignalId id) =>
        _bySignal.TryGetValue(id, out var list) ? list : Array.Empty<SignalChange>();
}
