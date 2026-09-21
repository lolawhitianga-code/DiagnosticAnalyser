using System.Text.Json;

namespace DiagFileMonitor.Core.Knowledge;

/// <summary>What CloudLog/maint_data.json knows about one output.</summary>
/// <param name="FullName">The machine's own name for it, e.g. "FloatingSide/DualGuns/UpperGunFire".</param>
/// <param name="ShortName">The last segment, which is what MachineLog.txt calls it minus the "IO-".</param>
/// <param name="Side">Read off the first segment of the name.</param>
public record OutputDuty(string FullName, string ShortName, MachineSide Side, int OnCount, int OffCount, double OnSeconds)
{
    /// <summary>Average seconds per energisation - a blunt but useful wear figure.</summary>
    public double AverageOnSeconds => OnCount == 0 ? 0 : OnSeconds / OnCount;
}

/// <summary>
/// Reads CloudLog/maint_data.json.
/// <para>
/// This file was written off early on as "probably never populated". It is populated, on both
/// Raked Wall Extruder V3 machines we have bundles for, and it is the only place in a bundle
/// where an output appears under its real name with its side attached. MachineLog.txt records
/// the same outputs as bare addresses with the side stripped off, so this file is what lets an
/// address be named.
/// </para>
/// <para>
/// It holds the current hour only, not the shift and not the life of the machine, so the counts
/// are small and they reset. Nothing here is a lifetime total; treat it as a one-hour sample.
/// Servos is present in the format and has been empty in every bundle seen so far.
/// </para>
/// </summary>
public static class MaintenanceCounters
{
    /// <summary>Everything the file says, or an empty list where there is no file to read.</summary>
    public static IReadOnlyList<OutputDuty> Read(string? bundleRoot)
    {
        if (string.IsNullOrWhiteSpace(bundleRoot)) return Array.Empty<OutputDuty>();

        var path = Path.Combine(bundleRoot, "CloudLog", "maint_data.json");
        if (!File.Exists(path)) return Array.Empty<OutputDuty>();

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return Array.Empty<OutputDuty>();
        }
    }

    /// <summary>When the counters were last rolled up, where the file says.</summary>
    public static DateTime? ReadStamp(string? bundleRoot)
    {
        if (string.IsNullOrWhiteSpace(bundleRoot)) return null;

        var path = Path.Combine(bundleRoot, "CloudLog", "maint_data.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Date", out var d)
                && d.ValueKind == JsonValueKind.String
                && DateTime.TryParse(d.GetString(), out var stamp))
                return stamp;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A file we cannot read is the same as no file, and says so by returning nothing.
        }

        return null;
    }

    public static IReadOnlyList<OutputDuty> Parse(string json)
    {
        var duties = new List<OutputDuty>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("TwoStates", out var states)
            || states.ValueKind != JsonValueKind.Object)
            return duties;

        foreach (var entry in states.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) continue;

            var name = entry.Name;
            var slash = name.LastIndexOf('/');
            var shortName = slash < 0 ? name : name[(slash + 1)..];

            duties.Add(new OutputDuty(
                name,
                shortName,
                SideOf(name),
                Number(entry.Value, "_onCount") is { } on ? (int)on : 0,
                Number(entry.Value, "_offCount") is { } off ? (int)off : 0,
                Number(entry.Value, "_onTime") ?? 0));
        }

        return duties;
    }

    /// <summary>
    /// The side is the first segment. ControlBox and ControlBox2 are the two operator stations,
    /// one per side of the machine, not a fixed/floating split - so they read as shared.
    /// </summary>
    private static MachineSide SideOf(string fullName)
    {
        var slash = fullName.IndexOf('/');
        var head = slash < 0 ? fullName : fullName[..slash];

        return head switch
        {
            "FixedSide" => MachineSide.FixedSide,
            "FloatingSide" => MachineSide.FloatingSide,
            "CommonIO" => MachineSide.Shared,
            _ when head.StartsWith("ControlBox", StringComparison.Ordinal) => MachineSide.Shared,
            _ => MachineSide.Unknown
        };
    }

    private static double? Number(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;
}
