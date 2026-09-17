namespace DiagFileMonitor.Core.Models;

/// <summary>
/// One I/O point known to exist on a machine, learned from its logs.
/// <para>
/// There is no other source for this. Machine.xml carries no I/O map, so the only way to know a
/// machine has an output called <c>TopStudClamp</c> at COM7-6.7 is to have seen it move. Keeping
/// what every bundle taught us means a later log can be read against the whole machine rather
/// than against the handful of points that happened to move in that one file - and a sensor that
/// never moves is usually the interesting one.
/// </para>
/// </summary>
public class MachineSignal
{
    public int Id { get; set; }

    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>The machine type as last reported, so the catalogue can be read per model too.</summary>
    public string MachineType { get; set; } = string.Empty;

    /// <summary>"Input" or "Output". Part of the identity: one address can be both.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>COM7-6.5, or 192.168.250.1-1.12. Kept as written; the format varies by controller.</summary>
    public string Address { get; set; } = string.Empty;

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>How many bundles this point has turned up in.</summary>
    public int BundlesSeenIn { get; set; }

    /// <summary>Every change ever seen, across every bundle. A point that barely moves stands out.</summary>
    public long TotalChanges { get; set; }
}
