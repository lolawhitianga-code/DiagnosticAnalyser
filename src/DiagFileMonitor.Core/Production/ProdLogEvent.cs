namespace DiagFileMonitor.Core.Production;

public enum ProdLogEventKind
{
    PanelStarted,
    PanelAssembled,
    PanelStopped,
    MemberAssembled,
    MemberCut,
    MachineStarted,
    MachineStopped,
    MachineIdleStart,
    MachineIdleStop,
    UserLogin,

    /// <summary>Not in the reference guide's event list, but real - seen in a support bundle's
    /// own production report.</summary>
    UserLogout,

    /// <summary>An event name we do not recognise. Counted and reported, never fatal.</summary>
    Unknown
}

/// <summary>
/// One line out of a ProdLogV2 weekly log.
/// <para>
/// The file is comma separated, one event per line. Field 1 is the event name and field 2 is
/// always a timestamp <c>yyyyMMdd HH:mm:ss</c> - the space is inside the field, not a separator.
/// </para>
/// </summary>
public class ProdLogEvent
{
    public ProdLogEventKind Kind { get; init; }

    /// <summary>The event name exactly as the file spelled it, so an unknown one can be reported.</summary>
    public string Name { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; }

    /// <summary>Every field after the timestamp, trimmed.</summary>
    public IReadOnlyList<string> Fields { get; init; } = Array.Empty<string>();

    public string SourceFile { get; init; } = string.Empty;
    public int LineNumber { get; init; }

    public string? Field(int index) => index < Fields.Count ? Fields[index] : null;

    public double Number(int index) =>
        double.TryParse(Field(index), out var value) ? value : 0;
}

/// <summary>What one file turned into, including everything that was not clean about it.</summary>
public class ProdLogParseResult
{
    public IReadOnlyList<ProdLogEvent> Events { get; init; } = Array.Empty<ProdLogEvent>();

    public string FileName { get; init; } = string.Empty;

    public int LinesRead { get; init; }

    /// <summary>
    /// Lines dropped because they were byte-identical to the line immediately before them.
    /// On real exports this is large - around half of all MemberAssembled lines and a third of
    /// MachineStopped - so it is reported rather than hidden.
    /// </summary>
    public int ConsecutiveDuplicates { get; init; }

    /// <summary>Lines that could not be read at all. Skipped, counted, never fatal.</summary>
    public int MalformedLines { get; init; }

    /// <summary>Event names not in <see cref="ProdLogEventKind"/>, with how many of each.</summary>
    public IReadOnlyDictionary<string, int> UnknownEventNames { get; init; } = new Dictionary<string, int>();

    /// <summary>True where the file carried the stray NUL padding some exports contain.</summary>
    public bool HadNullPadding { get; init; }

    public DateTime? FirstEvent => Events.Count > 0 ? Events[0].Timestamp : null;
    public DateTime? LastEvent => Events.Count > 0 ? Events[^1].Timestamp : null;
}
