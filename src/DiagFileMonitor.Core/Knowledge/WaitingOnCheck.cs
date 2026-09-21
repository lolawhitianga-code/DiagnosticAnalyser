using System.Text.RegularExpressions;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>One signal the waiting message named, and what that signal was actually doing.</summary>
public record WaitingSignal(
    SignalId Id,
    bool On,
    StateSource Source,
    TimeSpan? Since,
    int AddressesForThisName,
    string MissingPartnerAddress,
    bool PartnerFromTheMap)
{
    public string OnOff => On ? "on" : "off";

    /// <summary>
    /// This machine has a second one of these and it never spoke in this log. An input that never
    /// changes never appears in a change log, so the partner being absent is exactly what a sensor
    /// that never came on looks like.
    /// </summary>
    public bool PartnerMissing => MissingPartnerAddress.Length > 0;
}

/// <summary>An axis the waiting message named, and what it was doing.</summary>
public record WaitingAxis(string Name, string State, TimeSpan? Since)
{
    /// <summary>The only state that satisfies "in position".</summary>
    public bool Ready => State.Equals("OK", StringComparison.OrdinalIgnoreCase);
}

public class WaitingOnFindings
{
    /// <summary>The message, as the machine wrote it.</summary>
    public string Message { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    /// <summary>When it first said it, and when it last said it.</summary>
    public TimeSpan? From { get; init; }
    public TimeSpan? To { get; init; }

    public int Repeats { get; init; }

    /// <summary>True where the log ends on this message - the machine never got what it wanted.</summary>
    public bool StillWaitingAtTheEnd { get; init; }

    /// <summary>Signals named in the message that this log knows about, with their state.</summary>
    public IReadOnlyList<WaitingSignal> Named { get; init; } = Array.Empty<WaitingSignal>();

    /// <summary>Axes named in the message - a message about servos is half about motion.</summary>
    public IReadOnlyList<WaitingAxis> Axes { get; init; } = Array.Empty<WaitingAxis>();

    /// <summary>Names this machine logs at two addresses, one per side.</summary>
    public IReadOnlyList<string> PairedExamples { get; init; } = Array.Empty<string>();

    /// <summary>
    /// How many of this machine's confirmation inputs come as a matched pair, one per side. Used
    /// to judge whether a lone signal is missing its partner or is simply a single sensor.
    /// </summary>
    public int PairedNames { get; init; }
    public int LoneNames { get; init; }

    public IReadOnlyList<WaitingSignal> NotOn => Named.Where(s => !s.On).ToList();
    public IReadOnlyList<WaitingAxis> NotReady => Axes.Where(a => !a.Ready).ToList();

    /// <summary>Everything the message named that was not satisfied, plus any absent partner.</summary>
    public bool SomethingIsUnsatisfied =>
        NotOn.Count > 0 || NotReady.Count > 0 || Named.Any(s => s.PartnerMissing);

    public bool Any => Message.Length > 0 && (Named.Count > 0 || Axes.Count > 0);
}

/// <summary>
/// The machine says what it is waiting for. This says whether it got it.
/// <para>
/// From a real M21737 case. The log ends repeating <c>Waiting for Both Panel Height Servos in
/// position and PlateSupports Down</c>. Every servo reported OK, so the servos were not the
/// problem - and the report stopped there, because the message was the last thing it could see.
/// </para>
/// <para>
/// The answer was in the same file. <c>PlateSupportDown</c> is logged once, at
/// 192.168.250.1-1.1, and every other confirmation input on that machine appears at two addresses
/// - one per side. The second plate support's input never changed in the whole log, which for a
/// change log means it never came on. The machine was telling anybody who joined the two halves
/// up exactly what was wrong.
/// </para>
/// </summary>
public static class WaitingOnCheck
{
    /// <summary>Two words is a name. One generic word is a coincidence.</summary>
    private const int ShortestSingleWord = 4;

    /// <summary>
    /// Not anchored at the start. The machines write it as part of a longer line - "Step
    /// Condition, Waiting for Follower Arm UP" is the common shape, and anchoring missed 4,123
    /// of them in one sample log.
    /// </summary>
    private static readonly Regex Waiting = new(
        @"waiting\s+for\s+(?<what>.+?)['""]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Splits PlateSupportDown into Plate, Support, Down - and leaves Sensor1 alone.</summary>
    private static readonly Regex Words = new(
        @"[A-Z]+(?![a-z])|[A-Z][a-z]*|[a-z]+|\d+", RegexOptions.Compiled);

    public static WaitingOnFindings Check(
        IReadOnlyList<MachineLogEntry> entries, string? machineModel = null,
        ControlPlatform platform = ControlPlatform.Unknown)
    {
        if (entries.Count == 0) return new WaitingOnFindings();

        var waits = entries
            .Select(e => (Entry: e, Match: Waiting.Match(e.Description)))
            .Where(pair => pair.Match.Success)
            .ToList();

        if (waits.Count == 0) return new WaitingOnFindings();

        // The one it was still saying when the log ran out. That is the one that mattered.
        var last = waits[^1];
        var text = last.Match.Groups["what"].Value.Trim();

        var same = waits
            .Where(w => w.Entry.Description.Equals(last.Entry.Description, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var timeline = IoTimeline.Build(entries);
        var snapshot = timeline.AtLine(last.Entry.LineNumber);

        var addressesPerName = timeline.Signals
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Address).Distinct().ToList(),
                StringComparer.OrdinalIgnoreCase);

        var pairing = PairingHabit(timeline);

        var named = snapshot.Outputs.Concat(snapshot.Inputs)
            .Where(state => Mentions(text, state.Id.Name))
            .Select(state =>
            {
                var addresses = addressesPerName.GetValueOrDefault(state.Id.Name, new List<string>());
                var mapped = MachineIoMap.Find(machineModel, platform, state.Id.Kind, state.Id.Name);
                var fromMap = mapped.Count > 0;
                var partner = MissingPartner(machineModel, platform, state.Id, addresses, pairing, timeline);

                return new WaitingSignal(
                    state.Id,
                    state.On,
                    state.Source,
                    state.Since,
                    addresses.Count,
                    partner,
                    fromMap);
            })
            .OrderBy(s => s.On)
            .ThenBy(s => s.Id.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var axes = AxisStates(entries, last.Entry.LineNumber)
            .Where(axis => NamesAnAxis(text, axis.Name))
            .OrderBy(a => a.Ready)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WaitingOnFindings
        {
            Message = last.Entry.Description.Trim(),
            Tag = last.Entry.Tag,
            From = same[0].Entry.Time,
            To = same[^1].Entry.Time,
            Repeats = same.Count,
            StillWaitingAtTheEnd = last.Entry.LineNumber >= entries[^1].LineNumber - 3,
            Named = named,
            Axes = axes,
            PairedNames = addressesPerName.Count(p => p.Value.Count > 1),
            LoneNames = addressesPerName.Count(p => p.Value.Count == 1),
            PairedExamples = addressesPerName
                .Where(p => p.Value.Count > 1)
                .Select(p => $"{p.Key} ({string.Join(" and ", p.Value.OrderBy(a => a))})")
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    /// <summary>
    /// The address offset this machine uses between the two halves of a paired signal.
    /// <para>
    /// Derived from the machine's own log rather than assumed, because the habit differs: the
    /// M21737 extruder pairs 1.8 with 0.10, 1.6 with 0.8 and 1.4 with 0.6 - the same bit two
    /// lower on the module below - while the M22215 saw pairs adjacent bits on one module. Only
    /// an offset every pair agrees on counts.
    /// </para>
    /// </summary>
    private static (int Module, int Bit)? PairingHabit(IoTimeline timeline)
    {
        var offsets = timeline.Signals
            .Where(s => s.Kind == SignalKind.Input)
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(s => s.Address).Distinct().Count() == 2)
            .Select(g => g.Select(s => Address(s.Address)).Where(a => a is not null)
                .Select(a => a!.Value).OrderBy(a => a.Module).ThenBy(a => a.Bit).ToList())
            .Where(pair => pair.Count == 2)
            .Select(pair => (Module: pair[1].Module - pair[0].Module, Bit: pair[1].Bit - pair[0].Bit))
            .ToList();

        if (offsets.Count < 2) return null;

        var agreed = offsets.Distinct().ToList();
        return agreed.Count == 1 ? agreed[0] : null;
    }

    /// <summary>
    /// The address of this signal's other half, where the machine has one and it never spoke in
    /// this log.
    /// <para>
    /// The model's I/O map is asked first and believed absolutely, because it was read off a log
    /// long enough to exercise the whole machine. Only where there is no map does this fall back
    /// to the offset the log's own pairs agree on - and that fallback is a guess. On the M21737
    /// case it predicted 192.168.250.1-0.3 for the second plate support from three pairs that
    /// happened to share an offset. The real address is 192.168.250.1-2.7, and 0.3 is not used
    /// at all. The pairing offset on these machines is not one number: modules 0 and 1 pair two
    /// bits apart, module 1 pairs eleven bits apart internally, and module 4 pairs adjacent.
    /// </para>
    /// </summary>
    private static string MissingPartner(
        string? model, ControlPlatform platform, SignalId id, List<string> addressesHere,
        (int Module, int Bit)? habit, IoTimeline timeline)
    {
        if (addressesHere.Count != 1) return string.Empty;

        var known = MachineIoMap.Find(model, platform, id.Kind, id.Name);

        if (known.Count > 0)
        {
            // The map knows this machine. Anything it lists that did not move here is the answer.
            return known
                .Select(point => point.Address)
                .FirstOrDefault(address => !addressesHere.Contains(address, StringComparer.OrdinalIgnoreCase))
                ?? string.Empty;
        }

        return PartnerOf(id.Address, habit, timeline);
    }

    /// <summary>
    /// Where a partner would be if this machine used one offset throughout. A fallback for a
    /// model we have never mapped, and a guess - see <see cref="MissingPartner"/>.
    /// </summary>
    private static string PartnerOf(string address, (int Module, int Bit)? habit, IoTimeline timeline)
    {
        if (habit is not { } offset || Address(address) is not { } here) return string.Empty;

        // Try both directions - this signal could be either half of the pair.
        foreach (var candidate in new[]
                 {
                     (here.Module + offset.Module, here.Bit + offset.Bit),
                     (here.Module - offset.Module, here.Bit - offset.Bit)
                 })
        {
            if (candidate.Item1 < 0 || candidate.Item2 < 0) continue;

            var prefix = address[..address.LastIndexOf('-')];
            var guess = $"{prefix}-{candidate.Item1}.{candidate.Item2}";

            // Only worth naming if nothing is using it. An address already in the log is not a
            // missing partner, it is a different signal.
            if (timeline.Signals.Any(s => s.Kind == SignalKind.Input
                                          && s.Address.Equals(guess, StringComparison.OrdinalIgnoreCase)))
                continue;

            return guess;
        }

        return string.Empty;
    }

    /// <summary>Splits "192.168.250.1-1.8" into module 1, bit 8.</summary>
    private static (int Module, int Bit)? Address(string address)
    {
        var at = address.LastIndexOf('-');
        if (at < 0) return null;

        var parts = address[(at + 1)..].Split('.');

        return parts.Length == 2 && int.TryParse(parts[0], out var module) && int.TryParse(parts[1], out var bit)
            ? (module, bit)
            : null;
    }

    /// <summary>
    /// Each axis's last reported state at that moment.
    /// <para>
    /// A message about servos being in position is half a motion question, and motion never
    /// reaches the input and output lists.
    /// </para>
    /// </summary>
    private static List<WaitingAxis> AxisStates(IReadOnlyList<MachineLogEntry> entries, int upToLine)
    {
        var latest = new Dictionary<string, (string State, TimeSpan At)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (entry.LineNumber > upToLine) break;
            if (entry.Category != MachineLogCategory.MotionEvent) continue;

            // "FloatingSideHeight Status" and "FloatingSideHeight" are the same axis.
            var name = entry.Tag.EndsWith(" Status", StringComparison.OrdinalIgnoreCase)
                ? entry.Tag[..^" Status".Length]
                : entry.Tag;

            latest[name.Trim()] = (entry.Description.Trim(), entry.Time);
        }

        return latest
            .Select(pair => new WaitingAxis(pair.Key, Tidy(pair.Value.State), pair.Value.At))
            .ToList();
    }

    /// <summary>"Axis Disabled" and "Disabled" are the same answer.</summary>
    private static string Tidy(string state) =>
        state.StartsWith("Axis ", StringComparison.OrdinalIgnoreCase) ? state[5..] : state;

    /// <summary>
    /// Whether a waiting message names this signal.
    /// <para>
    /// Every word of the signal's name has to appear in the message, allowing for a plural,
    /// because the machine writes "PlateSupports Down" for an input called PlateSupportDown and
    /// "Follower Arm UP" for one called FollowerUp. Requiring all the words keeps PlateClampUp
    /// out of a message that only mentions plate supports.
    /// </para>
    /// </summary>
    public static bool Mentions(string message, string signalName)
    {
        var haystack = Normalise(message);

        var parts = Words.Matches(signalName)
            .Select(m => Normalise(m.Value))
            .Where(w => w.Length > 0)
            .ToList();

        if (parts.Count == 0) return false;

        // A single-word name has to be distinctive enough to mean something on its own.
        if (parts.Count == 1 && parts[0].Length < ShortestSingleWord) return false;

        return parts.All(word => haystack.Contains(word) || haystack.Contains(word + "s"));
    }

    /// <summary>
    /// Words too generic to identify an axis. "Waiting for Both Panel Height Servos" would
    /// otherwise match FloatingEjectServo, which is a different machine entirely.
    /// </summary>
    private static readonly string[] TooGeneric =
        { "servo", "servos", "axis", "axes", "motor", "drive", "status", "node", "side" };

    /// <summary>
    /// Whether a waiting message names this axis.
    /// <para>
    /// Looser than the signal rule, and it has to be. A message says "Panel Height Servos" and
    /// the axes are called FloatingSideHeight and TrolleyHeight - no wording joins all three
    /// words up, but "Height" identifies both of them and nothing else on the machine. So one
    /// distinctive word is enough, as long as it is not a word every axis shares.
    /// </para>
    /// </summary>
    public static bool NamesAnAxis(string message, string axisName)
    {
        var haystack = Normalise(message);

        return Words.Matches(axisName)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(word => word.Length >= ShortestSingleWord)
            .Where(word => !TooGeneric.Contains(word))
            .Any(word => haystack.Contains(word) || haystack.Contains(word + "s"));
    }

    private static string Normalise(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
