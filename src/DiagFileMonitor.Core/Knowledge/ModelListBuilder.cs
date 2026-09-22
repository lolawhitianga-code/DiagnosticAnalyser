using System.Text;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>
/// Turns a PLC tag export into the named-point list for a model.
/// <para>
/// The work is mostly grouping. These machines fit most things twice, one per side, and the PLC
/// says so in the tag name - FixedSide/PlateClamp and FloatingSide/PlateClamp, or PlateClamp1
/// and PlateClamp2. The log writes both as plain PlateClamp, so the two have to be folded back
/// into one named point fitted twice, which is the shape the rest of the app works in.
/// </para>
/// </summary>
public static class ModelListBuilder
{
    /// <summary>Prefixes and suffixes a PLC uses to say "this is the other side of the same thing".</summary>
    private static readonly (string Token, MachineSide Side)[] SideWords =
    {
        ("fixedside", MachineSide.FixedSide),
        ("fixed", MachineSide.FixedSide),
        ("floatingside", MachineSide.FloatingSide),
        ("floating", MachineSide.FloatingSide),
        ("commonio", MachineSide.Shared),
        ("common", MachineSide.Shared)
    };

    public static IReadOnlyList<KnownSignal> Build(IReadOnlyList<ImportedTag> tags)
    {
        var grouped = tags
            .Select(t => (Tag: t, Core: CoreName(t.BareName), Side: SideIn(t.BareName)))
            .GroupBy(x => (x.Tag.Kind, Key: MachineIoMap.Flatten(x.Core)))
            .ToList();

        var built = new List<KnownSignal>();

        foreach (var group in grouped)
        {
            var members = group.OrderBy(x => x.Side).ToList();

            // The display name keeps the log's shape: outputs carry IO-, inputs do not.
            var core = members[0].Core;
            var name = group.Key.Kind == SignalKind.Output && !core.StartsWith("IO-", StringComparison.OrdinalIgnoreCase)
                ? "IO-" + core
                : core;

            var points = members.Where(m => m.Tag.Point.Length > 0).Select(m => m.Tag.Point).ToList();
            var sides = members.Select(m => m.Side).ToList();

            built.Add(new KnownSignal(
                group.Key.Kind,
                name,
                members.Count,
                points,
                sides.Any(s => s != MachineSide.Unknown) ? sides : null));
        }

        return built
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The name with any side word and trailing instance number taken off.</summary>
    public static string CoreName(string name)
    {
        var cleaned = name;

        foreach (var (token, _) in SideWords)
        {
            cleaned = StripSegment(cleaned, token);
        }

        cleaned = cleaned.Trim('/', '_', '-', ' ');

        // A trailing number is left alone on purpose. StudPinUp2 is a second stud pin, not the
        // second side of StudPinUp, and the Raked Wall Extruder V3 has both - folding them would
        // silently lose a real point. If a PLC does number its sides that way instead of naming
        // them, this under-folds and somebody notices, which is the better way to be wrong.
        return cleaned.Length == 0 ? name : cleaned;
    }

    private static string StripSegment(string name, string token)
    {
        var flat = MachineIoMap.Flatten(name);
        var at = flat.IndexOf(token, StringComparison.Ordinal);
        if (at < 0) return name;

        // Walk the original string, skipping the characters that made up the token.
        var kept = new StringBuilder();
        var seen = 0;

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                var inToken = seen >= at && seen < at + token.Length;
                seen++;
                if (inToken) continue;
            }
            kept.Append(c);
        }

        return kept.ToString();
    }

    private static MachineSide SideIn(string name)
    {
        var flat = MachineIoMap.Flatten(name);

        foreach (var (token, side) in SideWords)
            if (flat.Contains(token, StringComparison.Ordinal))
                return side;

        return MachineSide.Unknown;
    }

    /// <summary>Writes the list as the C# the map is built from, ready to paste in.</summary>
    public static string ToCSharp(IReadOnlyList<KnownSignal> points)
    {
        var text = new StringBuilder();

        foreach (var point in points)
        {
            var seen = point.PointsSeen.Count > 0
                ? "new[] { " + string.Join(", ", point.PointsSeen.Select(p => $"\"{p}\"")) + " }"
                : "Array.Empty<string>()";

            var sides = point.Sides is { Count: > 0 } && point.Sides.Any(s => s != MachineSide.Unknown)
                ? ", new[] { " + string.Join(", ", point.Sides.Select(s => $"MachineSide.{s}")) + " }"
                : string.Empty;

            text.AppendLine($"        new(SignalKind.{point.Kind}, \"{point.Name}\", {point.Instances}, {seen}{sides}),");
        }

        return text.ToString();
    }
}
