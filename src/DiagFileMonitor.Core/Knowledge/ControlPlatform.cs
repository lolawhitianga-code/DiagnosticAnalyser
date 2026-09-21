using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// Which control system a machine is built on. Every Spida model ships in two versions and
/// <b>the node addresses differ between them</b>, so an I/O list from one is wrong for the other.
/// </summary>
public enum ControlPlatform
{
    /// <summary>Not enough in the log to tell. Nothing address-specific may be applied.</summary>
    Unknown,

    /// <summary>
    /// Addresses look like <c>192.168.250.1-4.2</c> and axes report as <c>Node0 Status</c> through
    /// <c>Node5 Status</c>. Seen on M20716, M21737 and M21844.
    /// </summary>
    NetworkNodes,

    /// <summary>
    /// Addresses look like <c>COM7-4.5</c> and axes report under their own names, e.g.
    /// <c>FloatingSidePuller Status</c>. Seen on AOR1694.
    /// </summary>
    SerialPort,

    /// <summary>
    /// Addresses carry a TCP prefix - <c>TCP192.168.50.2-3.17</c> - and axes report under their
    /// own names, e.g. <c>Axis-InfeedFollower</c>. Confirmed as the CLX build: support named
    /// M17311, a Tornado M500, as CLX and this is the shape its log writes.
    /// </summary>
    TcpAddressed
}

public record PlatformFinding(ControlPlatform Platform, string Evidence, int AddressesSeen)
{
    public bool Known => Platform != ControlPlatform.Unknown;

    public string Describe() => Platform switch
    {
        ControlPlatform.NetworkNodes => "network-addressed, axes report as numbered nodes",
        ControlPlatform.SerialPort => "serial-port addressed, axes report under their own names",
        ControlPlatform.TcpAddressed => "TCP-addressed (CLX), axes report under their own names",
        _ => "not established from this log"
    };
}

/// <summary>
/// Works out which control system a log came from, so an I/O map from the wrong one is never
/// applied.
/// <para>
/// This matters more than it looks. The I/O map is addresses, and handing a technician the right
/// name against the wrong address sends them to the wrong terminal. The map is therefore keyed
/// on the platform as well as the model, and a log whose platform cannot be established gets no
/// map at all rather than a plausible-looking wrong one.
/// </para>
/// <para>
/// The two are told apart by two independent signs that have always agreed so far: the shape of
/// the addresses, and whether axis status lines carry a node number or an axis name.
/// </para>
/// </summary>
public static class ControlPlatformCheck
{
    private static readonly Regex NetworkAddress = new(
        @"\(\d{1,3}(\.\d{1,3}){3}-\d+\.\d+\)", RegexOptions.Compiled);

    private static readonly Regex SerialAddress = new(
        @"\(COM\d+-\d+\.\d+\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TcpAddress = new(
        @"\(TCP\d{1,3}(\.\d{1,3}){3}-\d+\.\d+\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NodeStatus = new(
        @"^Node\d+ Status$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static PlatformFinding Detect(IReadOnlyList<MachineLogEntry> entries)
    {
        var network = 0;
        var serial = 0;
        var tcp = 0;
        var nodes = 0;

        foreach (var entry in entries)
        {
            // TCP is checked first: its address contains a bare IP and would otherwise match.
            if (TcpAddress.IsMatch(entry.Description)) tcp++;
            else if (NetworkAddress.IsMatch(entry.Description)) network++;
            else if (SerialAddress.IsMatch(entry.Description)) serial++;

            if (entry.Category == MachineLogCategory.MotionEvent && NodeStatus.IsMatch(entry.Tag.Trim()))
                nodes++;
        }

        if (tcp > network && tcp > serial && tcp > 0)
            return new PlatformFinding(
                ControlPlatform.TcpAddressed, $"{tcp} TCP-prefixed addresses", tcp);

        if (network > serial && network > 0)
            return new PlatformFinding(
                ControlPlatform.NetworkNodes,
                nodes > 0
                    ? $"{network} network addresses and {nodes} numbered node status lines"
                    : $"{network} network addresses",
                network);

        if (serial > 0)
            return new PlatformFinding(
                ControlPlatform.SerialPort,
                nodes == 0
                    ? $"{serial} COM-port addresses and no numbered node status lines"
                    : $"{serial} COM-port addresses",
                serial);

        return new PlatformFinding(ControlPlatform.Unknown, "no I/O addresses in this log", 0);
    }

    /// <summary>The platform an address belongs to, for checking a map against a log.</summary>
    public static ControlPlatform OfAddress(string address)
    {
        if (address.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return ControlPlatform.SerialPort;
        if (address.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) return ControlPlatform.TcpAddressed;

        var dash = address.IndexOf('-');
        var head = dash > 0 ? address[..dash] : address;

        return head.Count(c => c == '.') == 3 ? ControlPlatform.NetworkNodes : ControlPlatform.Unknown;
    }
}
