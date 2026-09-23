namespace DiagFileMonitor.Core.Production;

public class PanelClassifierOptions
{
    /// <summary>
    /// The longest a single panel's build can plausibly be. Anything past this is a missing stop
    /// event rather than a real duration - the M21737 sample has one panel logged at 304 minutes -
    /// so it is flagged and left out of time averages rather than counted at face value.
    /// </summary>
    public double MaxPanelBuildMinutes { get; init; } = 20;

    /// <summary>
    /// How close a PanelStopped has to be to a completion of the same name to be that panel being
    /// killed rather than an unrelated stop.
    /// </summary>
    public double StopMatchMinutes { get; init; } = 2;
}

public class PanelClassification
{
    public IReadOnlyList<PanelRecord> Panels { get; init; } = Array.Empty<PanelRecord>();

    /// <summary>PanelStarted re-issued for the panel already open. Common, and not a new panel.</summary>
    public int SameNameRestarts { get; init; }

    /// <summary>PanelAssembled rows that did not have the expected nine fields.</summary>
    public int UnexpectedFieldCounts { get; init; }
}

/// <summary>
/// Turns a stream of ProdLogV2 events into classified panels.
/// <para>
/// Two rules here come from real data rather than from the reference guide:
/// </para>
/// <list type="number">
/// <item>A PanelStarted for the panel already open is the HMI re-confirming, not a new panel. It
/// resets the build clock, because the log measures build minutes from the last start.</item>
/// <item>A panel left open when a different name starts is <b>superseded, not a fault</b>. Panel
/// names are reused labels. Counting these as abandonments produces a 37.7% fault rate on the
/// M21737 sample, against the 0.5-3% the delivered reports show.</item>
/// <item>On a Component Nailer each MembersSubAssembled is one finished component and is the
/// unit of output. A panel that produced components was worked, so it is not superseded, and the
/// PanelAssembled that closes it is not counted again on top of its components. Measured on
/// M21868: 5,117 components over 17 weeks against 5 PanelAssembled.</item>
/// </list>
/// </summary>
public class PanelClassifier
{
    private readonly PanelClassifierOptions _options;

    public PanelClassifier(PanelClassifierOptions? options = null) =>
        _options = options ?? new PanelClassifierOptions();

    public PanelClassification Classify(IEnumerable<ProdLogEvent> events)
    {
        var ordered = events.ToList();

        var panels = Collect(ordered, out var restarts, out var badFieldCounts);
        ApplyStops(ordered, panels);

        var counterLive = DaysTheFastenerCounterWasReporting(panels);

        return new PanelClassification
        {
            Panels = panels.Select(p => Judge(p, counterLive)).ToList(),
            SameNameRestarts = restarts,
            UnexpectedFieldCounts = badFieldCounts
        };
    }

    /// <summary>
    /// Every panel the log closed, before anything is judged.
    /// </summary>
    private List<PanelRecord> Collect(
        IReadOnlyList<ProdLogEvent> events, out int restarts, out int badFieldCounts)
    {
        var panels = new List<PanelRecord>();
        restarts = 0;
        badFieldCounts = 0;

        string? openName = null;
        DateTime? openStart = null;
        var members = 0;

        // Component Nailer: components finished inside the open panel, and when the current one
        // began - its first member placed, or failing that the last boundary.
        var components = 0;
        DateTime? componentStart = null;
        DateTime? lastBoundary = null;

        void CloseWithoutAssembly(DateTime when, string sourceFile)
        {
            if (openName is null) return;

            // Its components were built and are already counted: worked, not left open.
            if (components > 0)
            {
                openName = null;
                openStart = null;
                members = 0;
                components = 0;
                return;
            }

            panels.Add(new PanelRecord
            {
                Name = openName,
                StartedAt = openStart,
                EndedAt = when,
                Outcome = PanelOutcome.Superseded,
                MembersAssembled = members,
                SourceFile = sourceFile
            });

            openName = null;
            openStart = null;
            members = 0;
        }

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case ProdLogEventKind.PanelStarted:
                {
                    var name = e.Field(0) ?? string.Empty;

                    lastBoundary = e.Timestamp;
                    componentStart = null;

                    if (openName is null)
                    {
                        openName = name;
                        openStart = e.Timestamp;
                        members = 0;
                        components = 0;
                    }
                    else if (string.Equals(openName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        // Same panel re-confirmed. Keep it open; the build clock restarts.
                        restarts++;
                        openStart = e.Timestamp;
                    }
                    else
                    {
                        CloseWithoutAssembly(e.Timestamp, e.SourceFile);
                        openName = name;
                        openStart = e.Timestamp;
                        members = 0;
                        components = 0;
                    }

                    break;
                }

                case ProdLogEventKind.MemberAssembled:
                    if (openName is not null) members++;
                    componentStart ??= e.Timestamp;
                    break;

                case ProdLogEventKind.MembersSubAssembled:
                {
                    // Nothing placed and nothing fired is a stud passed through, so no clock runs
                    // for it; blocks fired with no member placed ran from the last boundary.
                    var fired = e.Number(1) > 0;
                    panels.Add(Component(e, openName, componentStart ?? (fired ? lastBoundary : null)));
                    components++;
                    componentStart = null;
                    lastBoundary = e.Timestamp;
                    break;
                }

                case ProdLogEventKind.PanelAssembled:
                {
                    // A Component Nailer closing a panel whose components are already counted.
                    if (components > 0)
                    {
                        openName = null;
                        openStart = null;
                        members = 0;
                        components = 0;
                        lastBoundary = e.Timestamp;
                        break;
                    }

                    // PanelAssembled, timestamp, fasteners, name, cube, lineal, build, idle, junctions
                    if (e.Fields.Count < 7)
                    {
                        badFieldCounts++;
                        break;
                    }

                    var build = e.Number(4);

                    panels.Add(new PanelRecord
                    {
                        Name = e.Field(1) ?? openName ?? string.Empty,
                        StartedAt = openStart,
                        EndedAt = e.Timestamp,
                        FastenerCount = e.Number(0),
                        MembersAssembled = members,
                        Cube = e.Number(2),
                        Lineal = e.Number(3),
                        BuildMinutes = build,
                        IdleMinutes = e.Number(5),
                        Junctions = e.Number(6),
                        BuildTimeImplausible = build > 20,
                        SourceFile = e.SourceFile
                    });

                    openName = null;
                    openStart = null;
                    members = 0;
                    break;
                }
            }
        }

        // A panel still open when the log ends is not evidence of anything - the week ran out.
        return panels;
    }

    /// <summary>
    /// One Component Nailer component.
    /// <c>MembersSubAssembled, time, blocks, fasteners, name, cube, length mm, name, cube, ...</c>
    /// <para>
    /// The log states no build time for a component, so it is measured from the first member
    /// placed for it (or the panel start or previous component, where blocks were fired with
    /// nothing placed) to the moment it closed. A stud with nothing placed and nothing fired gets
    /// no build time, so it reads as stepped past rather than as a fault. Field 2 is a nail counter that runs exactly one
    /// short of the nails the nogs need (2 per 90 mm nog, 3 per 140, 4 per 190) - see
    /// docs/prodlog-v2-members-sub-assembled.md. Stored raw and only used to tell whether anything
    /// fired; never report it as a nail count.
    /// Lengths here are millimetres, where MemberAssembled's are metres.
    /// </para>
    /// </summary>
    private PanelRecord Component(ProdLogEvent e, string? panelName, DateTime? startedAt)
    {
        var members = new List<(string Name, double Cube, double LengthMm)>();
        for (var i = 2; i + 2 < e.Fields.Count; i += 3)
            members.Add((e.Field(i) ?? string.Empty, e.Number(i + 1), e.Number(i + 2)));

        var stud = members.FirstOrDefault(m => m.Name.Contains("stud", StringComparison.OrdinalIgnoreCase));
        if (stud.Name is null && members.Count > 0) stud = members.MaxBy(m => m.LengthMm);
        var studName = stud.Name ?? string.Empty;

        var build = startedAt is { } start && e.Timestamp >= start ? (e.Timestamp - start).TotalMinutes : 0;

        return new PanelRecord
        {
            Kind = OutputKind.Component,
            Name = string.IsNullOrEmpty(panelName) ? studName
                : studName.Length == 0 ? panelName : $"{panelName} / {studName}",
            StartedAt = startedAt,
            EndedAt = e.Timestamp,
            FastenerCount = e.Number(1),
            MembersAssembled = members.Count,
            Cube = members.Sum(m => m.Cube),
            Lineal = members.Sum(m => m.LengthMm) / 1000,
            BuildMinutes = build,
            // Blocks nailed on - the component's joints, as junctions are a panel's.
            Junctions = e.Number(0),
            BuildTimeImplausible = build > _options.MaxPanelBuildMinutes,
            SourceFile = e.SourceFile
        };
    }

    /// <summary>
    /// Ties each PanelStopped to the completion it killed.
    /// <para>
    /// A stop does not become a record of its own. The controller writes PanelStopped and then
    /// still writes a PanelAssembled for the same panel a moment later, so the stop marks that
    /// completion rather than standing beside it. A stop matching no completion is dropped - it
    /// says nothing on its own.
    /// </para>
    /// </summary>
    private void ApplyStops(IReadOnlyList<ProdLogEvent> events, List<PanelRecord> panels)
    {
        var window = TimeSpan.FromMinutes(_options.StopMatchMinutes);

        foreach (var stop in events.Where(e => e.Kind == ProdLogEventKind.PanelStopped))
        {
            var name = stop.Field(0) ?? string.Empty;

            var match = panels
                .Where(p => p.Outcome != PanelOutcome.Superseded)
                .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .Where(p => (p.EndedAt - stop.Timestamp).Duration() <= window)
                .OrderBy(p => (p.EndedAt - stop.Timestamp).Duration())
                .FirstOrDefault();

            if (match is null) continue;

            panels[panels.IndexOf(match)] = match with { Stopped = true };
        }
    }

    /// <summary>
    /// The days the machine was demonstrably counting fasteners.
    /// <para>
    /// The counter is not always reporting. On a real extruder every panel built through one whole
    /// month carried zero fired despite real build time and real junctions, and the counter came
    /// back partway through a day. Applying a no-fasteners rule blindly would throw that month
    /// away as faults, so the rule is switched on per day: a day where no panel with real build
    /// time reports a single fastener is a day the counter was off, and build time alone decides.
    /// </para>
    /// </summary>
    internal static HashSet<DateOnly> DaysTheFastenerCounterWasReporting(IEnumerable<PanelRecord> panels)
    {
        var live = new HashSet<DateOnly>();

        foreach (var panel in panels)
        {
            if (panel.Outcome == PanelOutcome.Superseded) continue;
            if (panel.BuildMinutes > 0 && panel.FastenerCount > 0) live.Add(panel.Day);
        }

        return live;
    }

    /// <summary>
    /// What became of one panel. The order matters: stepped past is tested before anything is
    /// called a fault, because it is much the most common thing in the log and is not one.
    /// </summary>
    private static PanelRecord Judge(PanelRecord panel, HashSet<DateOnly> counterLive)
    {
        if (panel.Outcome == PanelOutcome.Superseded) return panel;

        var live = counterLive.Contains(panel.Day);
        panel = panel with { FastenerCounterLive = live };

        // Advanced on the HMI without being built: no time spent and nothing fired.
        if (panel.BuildMinutes <= 0 && panel.FastenerCount <= 0 && !panel.Stopped)
            return panel with { Outcome = PanelOutcome.SteppedPast };

        if (panel.Stopped)
            return panel with { Outcome = PanelOutcome.StoppedByOperator };

        // A zero count on a day the counter was off says nothing at all.
        if (panel.FastenerCount <= 0 && live)
        {
            return panel with
            {
                Outcome = panel.JunctionsMissing > 0
                    ? PanelOutcome.AbandonedPartWay
                    : PanelOutcome.RanButNailedNothing
            };
        }

        return panel with { Outcome = PanelOutcome.Completed };
    }
}
