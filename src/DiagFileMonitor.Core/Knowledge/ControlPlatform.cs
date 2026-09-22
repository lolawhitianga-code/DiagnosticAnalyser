using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// Which control system a machine is built on. Every Spida model ships in both, and they number
/// their I/O differently - so a point is found by <b>name</b>, and whatever number this machine
/// happens to use is read off its own log.
/// </summary>
public enum ControlPlatform
{
    /// <summary>Not enough in the log to tell.</summary>
    Unknown,

    /// <summary>
    /// ControlLogix. Addresses carry a transport prefix - <c>COM7-4.5</c> on the older serial
    /// protocol, <c>TCP192.168.50.2-3.17</c> since the move to TCP - and axes report under their
    /// own names, e.g. <c>Axis-InfeedFollower</c>. Seen on AOR1694 (COM) and M17311 (TCP).
    /// </summary>
    Clx,

    /// <summary>
    /// Omron. Addresses are a bare IP and a module.bit - <c>192.168.250.1-4.2</c> - and axes
    /// report as numbered EtherCAT nodes, <c>Node0 Status</c> through <c>Node5 Status</c>, as
    /// well as by name. Seen on M20716, M21737, M21844 and M20771.
    /// </summary>
    Omron
}

/// <summary>How the addresses are carried. A CLX detail; it does not change the machine.</summary>
public enum AddressTransport
{
    Unknown,
    ComPort,
    Tcp,
    Network
}

public record PlatformFinding(
    ControlPlatform Platform, AddressTransport Transport, string Evidence, int AddressesSeen)
{
    public bool Known => Platform != ControlPlatform.Unknown;

    public string Describe() => Platform switch
    {
        ControlPlatform.Clx when Transport == AddressTransport.ComPort => "CLX, older COM-port protocol",
        ControlPlatform.Clx => "CLX, TCP protocol",
        ControlPlatform.Omron => "Omron",
        _ => "not established from this log"
    };
}

/// <summary>
/// Works out which control system a log came from.
/// <para>
/// This is worth knowing for context and for reading the axis lines, but it is deliberately
/// <b>not</b> used to gate the I/O map any more. Machines of one model do not agree on their I/O
/// numbering even within one platform - some Wall Extruder DGs run their points on node 5 and
/// some on node 6 - so the map is a list of <b>names</b> and the numbers come from each log.
/// </para>
/// </summary>
public static class ControlPlatformCheck
{
    private static readonly Regex TcpAddress = new(
        @"\(TCP\d{1,3}(\.\d{1,3}){3}-\d+\.\d+\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NetworkAddress = new(
        @"\(\d{1,3}(\.\d{1,3}){3}-\d+\.\d+\)", RegexOptions.Compiled);

    private static readonly Regex ComAddress = new(
        @"\(COM\d+-\d+\.\d+\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NodeStatus = new(
        @"^Node\d+ Status$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static PlatformFinding Detect(IReadOnlyList<MachineLogEntry> entries)
    {
        var tcp = 0;
        var network = 0;
        var com = 0;
        var nodes = 0;

        foreach (var entry in entries)
        {
            // TCP first: its address contains a bare IP and would otherwise match that pattern.
            if (TcpAddress.IsMatch(entry.Description)) tcp++;
            else if (NetworkAddress.IsMatch(entry.Description)) network++;
            else if (ComAddress.IsMatch(entry.Description)) com++;

            if (entry.Category == MachineLogCategory.MotionEvent && NodeStatus.IsMatch(entry.Tag.Trim()))
                nodes++;
        }

        if (tcp >= network && tcp >= com && tcp > 0)
            return new PlatformFinding(ControlPlatform.Clx, AddressTransport.Tcp,
                $"{tcp} TCP-prefixed addresses", tcp);

        if (com >= network && com > 0)
            return new PlatformFinding(ControlPlatform.Clx, AddressTransport.ComPort,
                $"{com} COM-port addresses", com);

        if (network > 0)
            return new PlatformFinding(ControlPlatform.Omron, AddressTransport.Network,
                nodes > 0
                    ? $"{network} bare-IP addresses and {nodes} numbered node status lines"
                    : $"{network} bare-IP addresses",
                network);

        return new PlatformFinding(ControlPlatform.Unknown, AddressTransport.Unknown,
            "no I/O addresses in this log", 0);
    }

    /// <summary>The platform an address shape belongs to.</summary>
    public static ControlPlatform OfAddress(string address)
    {
        if (address.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) return ControlPlatform.Clx;
        if (address.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) return ControlPlatform.Clx;

        var dash = address.IndexOf('-');
        var head = dash > 0 ? address[..dash] : address;

        return head.Count(c => c == '.') == 3 ? ControlPlatform.Omron : ControlPlatform.Unknown;
    }

    /// <summary>
    /// The node and address, with the port stripped off. Spida's terms: an address like
    /// <c>TCP192.168.50.5-3.6</c> is port 192.168.50.5, node 3, address 6, and it is the node and
    /// address a technician reads off the cabinet.
    /// </summary>
    public static string NodeAndAddress(string address)
    {
        var dash = address.LastIndexOf('-');
        return dash >= 0 && dash < address.Length - 1 ? address[(dash + 1)..] : address;
    }

    /// <summary>Kept for callers that still say Point; the same thing.</summary>
    public static string Point(string address) => NodeAndAddress(address);
}
