namespace DiagFileMonitor.Core.Production;

public class PanelClassifierOptions
{
    /// <summary>
    /// The longest a single panel's build can plausibly be. Anything past this is a missing stop
    /// event rather than a real duration - the M21737 sample has one panel logged at 304 minutes -
    /// so it is flagged and left out of time averages rather than counted at face value.
    /// </summary>
    public double MaxPanelBuildMinutes { get; init; } = 20;
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
/// </list>
/// </summary>
public class PanelClassifier
{
    private readonly PanelClassifierOptions _options;

    public PanelClassifier(PanelClassifierOptions? options = null) =>
        _options = options ?? new PanelClassifierOptions();

    public PanelClassification Classify(IEnumerable<ProdLogEvent> events)
    {
        var panels = new List<PanelRecord>();
        int restarts = 0, badFieldCounts = 0;

        string? openName = null;
        DateTime? openStart = null;
        var members = 0;

        void CloseWithoutAssembly(PanelOutcome outcome, DateTime when, string sourceFile)
        {
            if (openName is null) return;

            panels.Add(new PanelRecord
            {
                Name = openName,
                StartedAt = openStart,
                EndedAt = when,
                Outcome = outcome,
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

                    if (openName is null)
                    {
                        openName = name;
                        openStart = e.Timestamp;
                        members = 0;
                    }
                    else if (string.Equals(openName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        // Same panel re-confirmed. Keep it open; the build clock restarts.
                        restarts++;
                        openStart = e.Timestamp;
                    }
                    else
                    {
                        CloseWithoutAssembly(PanelOutcome.Superseded, e.Timestamp, e.SourceFile);
                        openName = name;
                        openStart = e.Timestamp;
                        members = 0;
                    }

                    break;
                }

                case ProdLogEventKind.MemberAssembled:
                    if (openName is not null) members++;
                    break;

                case ProdLogEventKind.PanelStopped:
                    CloseWithoutAssembly(PanelOutcome.StoppedByOperator, e.Timestamp, e.SourceFile);
                    break;

                case ProdLogEventKind.PanelAssembled:
                {
                    // PanelAssembled, timestamp, fasteners, name, cube, lineal, build, idle, junctions
                    if (e.Fields.Count < 7)
                    {
                        badFieldCounts++;
                        break;
                    }

                    var fasteners = e.Number(0);
                    var build = e.Number(4);

                    panels.Add(new PanelRecord
                    {
                        Name = e.Field(1) ?? openName ?? string.Empty,
                        StartedAt = openStart,
                        EndedAt = e.Timestamp,
                        // Nothing fired and nothing assembled means the operator advanced past it.
                        Outcome = fasteners > 0 || members > 0
                            ? PanelOutcome.Completed
                            : PanelOutcome.SteppedPast,
                        FastenerCount = fasteners,
                        MembersAssembled = members,
                        Cube = e.Number(2),
                        Lineal = e.Number(3),
                        BuildMinutes = build,
                        IdleMinutes = e.Number(5),
                        Junctions = e.Number(6),
                        BuildTimeImplausible = build > _options.MaxPanelBuildMinutes,
                        SourceFile = e.SourceFile
                    });

                    openName = null;
                    openStart = null;
                    members = 0;
                    break;
                }
            }
        }

        // A panel still open when the log ends is not evidence of anything - the week simply ran
        // out. It is dropped rather than recorded as abandoned.

        return new PanelClassification
        {
            Panels = panels,
            SameNameRestarts = restarts,
            UnexpectedFieldCounts = badFieldCounts
        };
    }
}
