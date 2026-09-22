using System.Text;
using System.Xml.Linq;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One I/O point as the machine's own configuration defines it.</summary>
/// <param name="Name">The leaf element name, e.g. PlateSupportDown.</param>
/// <param name="Path">Where it sits, e.g. FixedSide / Inputs / PlateSupportDown.</param>
/// <param name="Address">Port, node and address joined the way the log writes them - TCP192.168.50.5-3.6 is port 192.168.50.5, node 3, address 6.</param>
/// <param name="Fitted">InUse. False means this machine does not have it at all.</param>
/// <param name="Inverted">The signal is read the other way up.</param>
/// <param name="Simulated">Faked in software - it is not a real sensor on this machine.</param>
public record ConfiguredSignal(
    SignalKind Kind,
    string Name,
    string Path,
    MachineSide Side,
    string Address,
    bool Fitted,
    bool Inverted,
    bool Simulated,
    bool SubsystemFitted = true)
{
    /// <summary>
    /// Actually on this machine. A point can be InUse inside a subsystem the machine does not
    /// have - the file carries the definitions either way - so both have to be true.
    /// </summary>
    public bool OnThisMachine => Fitted && SubsystemFitted;
}

/// <summary>An axis, and which node it answers on.</summary>
public record ConfiguredAxis(string Name, int Node, string Port, double Velocity, double Accel, double Scale);

public record MachineConfig(
    IReadOnlyList<ConfiguredSignal> Signals,
    IReadOnlyList<ConfiguredAxis> Axes,
    IReadOnlyList<string> Ports)
{
    public bool Any => Signals.Count > 0;

    public IEnumerable<ConfiguredSignal> Fitted => Signals.Where(s => s.OnThisMachine);

    /// <summary>The subsystem flags at the root, e.g. MajorSubFitted, CClampsFitted.</summary>
    public IReadOnlyDictionary<string, bool> Subsystems { get; init; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Points defined in the file for kit this machine does not have.</summary>
    public int DefinedButNotOnThisMachine => Signals.Count(s => s.Fitted && !s.SubsystemFitted);

    /// <summary>The control platforms the fitted points sit on, with how many on each.</summary>
    public IReadOnlyDictionary<ControlPlatform, int> PlatformsUsed => Fitted
        .GroupBy(s => ControlPlatformCheck.OfAddress(s.Address))
        .Where(g => g.Key != ControlPlatform.Unknown)
        .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// A machine is built on one control system. The one exception Spida allow is a CLX printer
    /// on an Omron machine, so a handful of printer points on the other platform is expected and
    /// anything else is not.
    /// <para>
    /// This is worth checking rather than assuming, because a mixed reading almost always means
    /// the reading is wrong. Taking each point's InUse flag without its option's Fitted flag made
    /// an M21844 look like an Omron machine running two CLX subsystems; it runs neither, and the
    /// definitions belong to options it has not got.
    /// </para>
    /// </summary>
    public bool PlatformLooksWrong => PlatformsUsed.Count > 1 && OffPlatformNonPrinters.Count > 0;

    /// <summary>Fitted points on the minority platform that are not a printer.</summary>
    public IReadOnlyList<ConfiguredSignal> OffPlatformNonPrinters
    {
        get
        {
            if (PlatformsUsed.Count <= 1) return Array.Empty<ConfiguredSignal>();

            var main = PlatformsUsed.OrderByDescending(p => p.Value).First().Key;

            return Fitted
                .Where(s => ControlPlatformCheck.OfAddress(s.Address) != main)
                .Where(s => ControlPlatformCheck.OfAddress(s.Address) != ControlPlatform.Unknown)
                .Where(s => !IsPrinter(s))
                .ToList();
        }
    }

    private static bool IsPrinter(ConfiguredSignal signal) =>
        signal.Path.Contains("print", StringComparison.OrdinalIgnoreCase)
        || signal.Name.Contains("print", StringComparison.OrdinalIgnoreCase)
        || signal.Path.Contains("TimPrint", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The port carrying most of the machine's I/O. The log's NodeN Status lines come from this
    /// controller, so it is the one a node number should be read against.
    /// </summary>
    public string MainPort => Signals
        .Where(s => s.Fitted)
        .GroupBy(s => s.Address[..Math.Max(0, s.Address.LastIndexOf('-'))])
        .OrderByDescending(g => g.Count())
        .Select(g => g.Key)
        .FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// What the machine calls the axis on a node - Node0 is the fixed side trolley, and so on.
    /// <para>
    /// Port-aware, because node numbers are reused across controllers. On an M21844 node 4 is
    /// TrolleyHeight on the Omron main and the fixed side servo guns on a CLX at
    /// TCP192.168.50.2. Answering without the port picks whichever came first in the file.
    /// </para>
    /// </summary>
    public string? AxisOnNode(int node, string? port = null)
    {
        var wanted = port ?? MainPort;

        return Axes
            .Where(a => a.Node == node && a.Velocity > 0)
            .Where(a => wanted.Length == 0 || a.Port.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .FirstOrDefault();
    }
}

/// <summary>
/// Reads the model configuration XML a bundle carries - RakingWallExtruderV3DG.xml and its kin.
/// <para>
/// This is the wiring list, and it had been sitting in every bundle unread. It is UTF-16, which
/// is why it looked empty to anything expecting UTF-8. It gives, for every point: the name, the
/// side, the port, node and address, whether it is <b>fitted at all</b>, whether it is inverted,
/// and whether it is simulated. It also gives each axis its node number.
/// </para>
/// <para>
/// Held against an M21844 log it agrees on the name at 80 of the 82 points both carry, and the
/// two it does not are the E-stop group, where the config uses a generic element name and keeps
/// the real one in EStopName. So this is the authority and the log is the check, not the other
/// way round.
/// </para>
/// <para>
/// The thing it gives that no log can: <b>InUse</b>. A log records changes, so a sensor that
/// never came on and one that is not fitted look identical - which is exactly what cost us the
/// M21737 case. The config tells the two apart outright.
/// </para>
/// <para>
/// A point is identified by <b>port, node and address together</b>. One machine can run more than
/// one port: an M21844 has its own I/O on 192.168.250.1 and a MajorSubInfeed subsystem on
/// TCP192.168.50.2:2, and the two reuse node numbers. Matching on node and address alone puts
/// the infeed's bay proximity switches on top of the extruder's plate supports.
/// </para>
/// </summary>
public static class MachineConfigIo
{
    private static readonly Dictionary<string, MachineSide> SideByRoot = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FixedSide"] = MachineSide.FixedSide,
        ["FloatingSide"] = MachineSide.FloatingSide,
        ["CommonIO"] = MachineSide.Shared,
        ["ControlBox"] = MachineSide.Shared,
        ["ControlBox2"] = MachineSide.Shared,
        ["EStops"] = MachineSide.Shared
    };

    public static MachineConfig Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Empty;

        try
        {
            return Parse(ReadText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return Empty;
        }
    }

    private static readonly MachineConfig Empty = new(
        Array.Empty<ConfiguredSignal>(), Array.Empty<ConfiguredAxis>(), Array.Empty<string>());

    /// <summary>These are UTF-16 with a BOM. Reading them as UTF-8 gives an empty document.</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        return Encoding.UTF8.GetString(bytes);
    }

    public static MachineConfig Parse(string xml)
    {
        // The declaration says utf-16 but the string is already decoded, so it has to go.
        var body = xml.TrimStart('﻿');
        var declaration = body.IndexOf("?>", StringComparison.Ordinal);
        if (body.StartsWith("<?xml", StringComparison.Ordinal) && declaration > 0)
            body = body[(declaration + 2)..];

        var root = XElement.Parse(body);
        var signals = new List<ConfiguredSignal>();
        var axes = new List<ConfiguredAxis>();

        // Subsystem flags sit at the root: MajorSubFitted, CClampsFitted, CenterGunFitted and so
        // on. The file defines every point of every option whether or not the machine has it, so
        // these gate the lot. M21844 carries 21 MajorSubInfeed points all marked InUse and has no
        // infeed at all - MajorSubFitted is false.
        var subsystems = root.Elements()
            .Where(e => e.Name.LocalName.EndsWith("Fitted", StringComparison.Ordinal))
            .ToDictionary(
                e => e.Name.LocalName[..^"Fitted".Length],
                e => (e.Value ?? string.Empty).Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        Walk(root, new List<string>(), signals, axes);

        var gated = signals
            .Select(s => s with { SubsystemFitted = SubsystemOf(s.Path, subsystems) })
            .ToList();

        return new MachineConfig(
            gated,
            axes,
            gated.Select(s => Port(s.Address)).Where(p => p.Length > 0).Distinct().ToList())
        {
            Subsystems = subsystems
        };
    }

    private static void Walk(
        XElement element, List<string> path,
        List<ConfiguredSignal> signals, List<ConfiguredAxis> axes)
    {
        var children = element.Elements().ToList();

        if (children.Any(c => c.Name.LocalName is "NodeNum" or "Address"))
        {
            Add(path, children.ToDictionary(c => c.Name.LocalName, c => (c.Value ?? string.Empty).Trim()),
                signals, axes);
            return;
        }

        foreach (var child in children)
        {
            path.Add(child.Name.LocalName);
            Walk(child, path, signals, axes);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static void Add(
        List<string> path, Dictionary<string, string> fields,
        List<ConfiguredSignal> signals, List<ConfiguredAxis> axes)
    {
        var node = Number(fields, "NodeNum");

        // An axis carries motion settings; an I/O point does not.
        if (fields.ContainsKey("Velocity") || fields.ContainsKey("Scale"))
        {
            axes.Add(new ConfiguredAxis(
                string.Join(" ", path.Where(p => p is not ("Axis" or "ServoGun"))),
                (int)(node ?? 0),
                fields.GetValueOrDefault("Port", string.Empty).Trim(),
                Number(fields, "Velocity") ?? 0,
                Number(fields, "Accel") ?? 0,
                Number(fields, "Scale") ?? 0));
            return;
        }

        var kind = KindOf(path);
        var where = Where(fields.GetValueOrDefault("Address", string.Empty), (int?)node);
        if (kind is null || where is null) return;

        var port = fields.GetValueOrDefault("Port", string.Empty).Trim();

        signals.Add(new ConfiguredSignal(
            kind.Value,
            path[^1],
            string.Join(" / ", path),
            SideByRoot.GetValueOrDefault(path[0], MachineSide.Unknown),
            $"{port}-{where.Value.Node}.{where.Value.Address}",
            Flag(fields, "InUse"),
            Flag(fields, "Inverted"),
            Flag(fields, "Simulate")));
    }

    /// <summary>
    /// Reads an Address field into a node and an address.
    /// <para>
    /// Usually the field is a plain address and the node comes from NodeNum beside it. But the
    /// Tornado's analogue inputs are written "3:6", and that is <b>node 3, address 6</b> - the
    /// field carries its own node, and the NodeNum element beside it reads 0 and means nothing.
    /// One of the eight is written "3.1" instead, so the file is not consistent with itself and
    /// both separators have to be read the same way.
    /// </para>
    /// <para>
    /// Getting this wrong is not harmless: parsing "3:6" as a plain number fails and the point is
    /// dropped without a word, which was losing all eight of the Tornado's analogues -
    /// FollowerDistance and InfeedLaserDistance among them.
    /// </para>
    /// </summary>
    private static (int Node, int Address)? Where(string address, int? nodeNum)
    {
        var text = address.Trim();
        if (text.Length == 0) return null;

        if (int.TryParse(text, out var plain))
            return nodeNum is null ? null : ((int)nodeNum, plain);

        var parts = text.Split(':', '.');

        return parts.Length == 2
               && int.TryParse(parts[0], out var node)
               && int.TryParse(parts[1], out var point)
            ? (node, point)
            : null;
    }

    private static SignalKind? KindOf(List<string> path)
    {
        foreach (var segment in path)
        {
            if (segment is "Outputs" or "Output" or "AnalogOutputs") return SignalKind.Output;
            if (segment is "Inputs" or "Input" or "AnalogInputs") return SignalKind.Input;
        }

        return null;
    }

    /// <summary>
    /// Whether the option a point belongs to is fitted. Matched on the leading path segment -
    /// MajorSubInfeed and MajorSubLifter are both gated by MajorSubFitted, CClamp by
    /// CClampsFitted. A section with no matching flag is taken as fitted, because most of the
    /// machine has no flag and gating it away would empty the list.
    /// </summary>
    private static bool SubsystemOf(string path, IReadOnlyDictionary<string, bool> flags)
    {
        var head = path.Split(' ').FirstOrDefault() ?? string.Empty;
        head = head.Split('/')[0].Trim();

        foreach (var (name, fitted) in flags)
        {
            var stem = name.TrimEnd('s');
            if (head.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                || head.Equals(name, StringComparison.OrdinalIgnoreCase))
                return fitted;
        }

        return true;
    }

    private static string Port(string address)
    {
        var dash = address.LastIndexOf('-');
        return dash > 0 ? address[..dash] : string.Empty;
    }

    private static bool Flag(Dictionary<string, string> fields, string name) =>
        fields.GetValueOrDefault(name, string.Empty).Equals("true", StringComparison.OrdinalIgnoreCase);

    private static double? Number(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var text)
        && double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
