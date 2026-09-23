namespace DiagFileMonitor.Core.Production;

public enum PanelOutcome
{
    /// <summary>Real work: fasteners fired, or members assembled, or both.</summary>
    Completed,

    /// <summary>
    /// Assembled with nothing in it. The operator advanced the HMI past a panel that did not need
    /// building. Routine workflow, not a fault, and it must be kept out of any fault rate.
    /// </summary>
    SteppedPast,

    /// <summary>
    /// The operator stopped an open panel. This is the one unambiguous abandonment signal in the
    /// log - the controller wrote PanelStopped on purpose.
    /// </summary>
    StoppedByOperator,

    /// <summary>
    /// The machine spent time on it and fired nothing, on a day the fastener counter was working.
    /// </summary>
    RanButNailedNothing,

    /// <summary>
    /// Nothing fired and junctions left undone - it was given up on part way through.
    /// </summary>
    AbandonedPartWay,

    /// <summary>
    /// A panel was open and a different panel name started, so this one never closed.
    /// <para>
    /// NOT counted as a fault. Panel names on these machines are reused labels rather than unique
    /// identifiers - on the M21737 sample, 426 distinct names across 10,364 PanelStarted events,
    /// with "E5" started 139 times and assembled 53 times. Treating every unclosed start as an
    /// abandoned panel gives a 37.7% fault rate on real data, which is operators moving around the
    /// HMI, not broken machinery. See docs/production-reports.md.
    /// </para>
    /// </summary>
    Superseded
}

/// <summary>What one record stands for.</summary>
public enum OutputKind
{
    /// <summary>A wall panel, closed by PanelAssembled.</summary>
    Panel,

    /// <summary>
    /// One component off a Component Nailer - a stud with its blocks or noggings - closed by
    /// MembersSubAssembled. On those machines this, not the panel, is the unit of output.
    /// </summary>
    Component
}

/// <summary>One panel the machine worked on, after classification.</summary>
public record PanelRecord
{
    /// <summary>A panel, or a Component Nailer's component. Counted the same way either way.</summary>
    public OutputKind Kind { get; init; } = OutputKind.Panel;

    /// <summary>Panel label from the log. Reused across jobs - never treat it as unique.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>When the panel closed, however it closed.</summary>
    public DateTime EndedAt { get; init; }

    /// <summary>The last PanelStarted for this panel, where the log had one.</summary>
    public DateTime? StartedAt { get; init; }

    public PanelOutcome Outcome { get; init; }

    /// <summary>
    /// Field 3 of PanelAssembled. The reference guide calls this nailsFired.
    /// <para>
    /// UNVERIFIED on this data. The guide says it runs about 4x the member count; on the M21737
    /// sample only 5.7% of panels matched that and the mean ratio was 2.86. It is reported as a
    /// raw count and never converted into anything else.
    /// </para>
    /// </summary>
    public double FastenerCount { get; init; }

    public int MembersAssembled { get; init; }

    /// <summary>Cubic metres of timber in the panel.</summary>
    public double Cube { get; init; }

    /// <summary>Lineal metres of timber in the panel.</summary>
    public double Lineal { get; init; }

    /// <summary>
    /// Build minutes as the log states them, measured from the last PanelStarted. Never
    /// recomputed from wall-clock time between events.
    /// </summary>
    public double BuildMinutes { get; init; }

    public double IdleMinutes { get; init; }

    /// <summary>Field 9 of PanelAssembled, which the reference guide calls junctions.</summary>
    public double Junctions { get; init; }

    /// <summary>
    /// Junctions the panel was short of, where the log says how many were completed. ProdLogV2
    /// does not carry that, so it stays null there.
    /// </summary>
    public double? JunctionsMissing { get; init; }

    /// <summary>
    /// Whether the fastener counter was reporting on the day this panel was built. Where it was
    /// not, a zero count says nothing and build time alone decides whether the panel was made.
    /// </summary>
    public bool FastenerCounterLive { get; init; }

    /// <summary>
    /// True where BuildMinutes is past the plausible ceiling, which means a missing stop event
    /// rather than a panel that really took that long. Excluded from time averages.
    /// </summary>
    public bool BuildTimeImplausible { get; init; }

    public string SourceFile { get; init; } = string.Empty;

    /// <summary>The operator pressed Panel Stopped on this one before it finished.</summary>
    public bool Stopped { get; init; }

    /// <summary>Real work happened, whatever else did.</summary>
    public bool DidWork => Outcome == PanelOutcome.Completed;

    /// <summary>
    /// The machine was asked for this panel and did not make it. Stepped past is not a fault - the
    /// operator advancing the job list - and superseded is not either, since panel names are reused
    /// labels and an unclosed start is somebody moving around the HMI.
    /// </summary>
    public bool IsFault => Outcome is PanelOutcome.StoppedByOperator
        or PanelOutcome.RanButNailedNothing
        or PanelOutcome.AbandonedPartWay;

    /// <summary>Why it is recorded the way it is, for the report's own words.</summary>
    public string Explain() => Outcome switch
    {
        PanelOutcome.SteppedPast =>
            $"advanced on the HMI without being built - {Junctions:0} junction(s), {Lineal:0.##} m",
        PanelOutcome.StoppedByOperator =>
            "Panel Stopped was pressed before it finished",
        PanelOutcome.AbandonedPartWay =>
            $"nothing fired, and {JunctionsMissing:0} of {Junctions:0} junction(s) left undone",
        PanelOutcome.RanButNailedNothing =>
            BuildMinutes > 0
                ? $"spent {BuildMinutes:0.#} min building and fired nothing across {Junctions:0} junction(s)"
                : $"nothing fired across {Junctions:0} junction(s)",
        PanelOutcome.Superseded =>
            "left open when a different panel name started - the operator moving around the HMI",
        _ => "built"
    };

    public DateOnly Day => DateOnly.FromDateTime(EndedAt);
}
