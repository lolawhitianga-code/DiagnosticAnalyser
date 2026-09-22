using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Services;

/// <summary>The three Spida logs read off a stored bundle, plus anything odd about finding them.</summary>
public class BundleLogSet
{
    public IReadOnlyList<MachineLogEntry> MachineLog { get; init; } = Array.Empty<MachineLogEntry>();
    public IReadOnlyList<ErrLogEntry> ErrLog { get; init; } = Array.Empty<ErrLogEntry>();
    public IReadOnlyList<ChangeLogEntry> ChangeLog { get; init; } = Array.Empty<ChangeLogEntry>();

    /// <summary>The machine's own configuration file, which says what electronics each axis runs on.</summary>
    public string MachineConfigPath { get; init; } = string.Empty;

    /// <summary>
    /// Notes about which file was picked where a bundle carried more than one candidate, so an
    /// ambiguous export is reported rather than silently resolved.
    /// </summary>
    public IReadOnlyList<string> SelectionNotes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Finds and reads the logs inside an unpacked bundle.
/// <para>
/// Picking a log is not just a filename match. An export can carry more than one file called
/// MachineLog.txt - the live one under the dated support folder and a stale placeholder at the
/// root of the SDN install - and reading the wrong one means analysing a machine's history from
/// months ago while believing it is from this morning. Logs also moved between SDN versions,
/// leaving old copies behind. So the freshest copy wins - newest entry, or for MachineLog the
/// newest file - then the support folder, depth and size break a tie; and when there was a
/// choice to make, the report says so.
/// </para>
/// </summary>
public static class BundleLogs
{
    public static BundleLogSet Read(DiagnosticFile bundle)
    {
        var notes = new List<string>();

        return new BundleLogSet
        {
            MachineLog = MachineLogFile.ParseFile(PathOf(bundle, LogFileKind.MachineLog, notes)),
            ErrLog = ErrLogFile.ParseFile(PathOf(bundle, LogFileKind.ErrorLog, notes)),
            ChangeLog = ChangeLogFile.ParseFile(PathOf(bundle, LogFileKind.ChangeLog, notes)),
            MachineConfigPath = MachineConfigPath(bundle),
            SelectionNotes = notes
        };
    }

    /// <summary>
    /// The best candidate for a log kind. Exposed so callers that only want one file do not have
    /// to parse all three.
    /// </summary>
    public static string PathOf(DiagnosticFile bundle, LogFileKind kind, List<string>? notes = null)
    {
        var candidates = bundle.LogFiles.Where(l => l.Kind == kind).ToList();
        if (candidates.Count == 0) return string.Empty;

        var best = candidates.Count == 1 ? candidates[0] : Newest(candidates, kind);

        if (candidates.Count > 1 && notes is not null)
        {
            var others = candidates.Count - 1;
            notes.Add($"This bundle carries {candidates.Count} files called {best.FileName}. "
                      + $"Read {Describe(best.FullPath)}; ignored {others} other"
                      + (others == 1 ? "." : "s."));
        }

        return best.FullPath;
    }

    /// <summary>
    /// The machine's own configuration file, named after the model - RakingWallExtruderV3DG.xml,
    /// TornadoM450.xml and so on - rather than the generic Machine.xml. A bundle can carry several,
    /// including an empty one beside a .xmlTmp, so the model's own name is matched first and the
    /// largest readable file wins.
    /// </summary>
    public static string MachineConfigPath(DiagnosticFile bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.MachineType)) return string.Empty;

        return bundle.LogFiles
            .Where(l => l.SizeBytes > 0)
            .Where(l => l.FileName.StartsWith(bundle.MachineType, StringComparison.OrdinalIgnoreCase)
                        && l.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.SizeBytes)
            .FirstOrDefault()?.FullPath ?? string.Empty;
    }

    /// <summary>
    /// Logs moved between SDN versions and the old copies stay behind. M20771 carried two
    /// Change.logs: the deeper Logs/Support copy stopped in March, the one at the root ran to
    /// that morning and held a fixed side puller home position change. Reading the old one says
    /// "nothing changed" - exactly the wrong answer. So the freshest copy wins, wherever it sits:
    /// <list type="bullet">
    /// <item>Change.log and ErrLog lines are dated, so the copy with the newest entry.</item>
    /// <item>MachineLog lines carry a time but no date, so the copy last written - extraction
    /// keeps each file's time from the zip.</item>
    /// </list>
    /// A copy with nothing in it never beats one with something in it, and the folder rules only
    /// break a tie.
    /// </summary>
    private static ExtractedLogFile Newest(List<ExtractedLogFile> candidates, LogFileKind kind) =>
        candidates
            .Select(l => (Log: l, Fresh: Freshness(l.FullPath, kind)))
            .OrderByDescending(c => c.Fresh.HasContent)
            .ThenByDescending(c => c.Fresh.Newest ?? DateTime.MinValue)
            .ThenByDescending(c => InSupportFolder(c.Log.FullPath))
            .ThenByDescending(c => Depth(c.Log.FullPath))
            .ThenByDescending(c => c.Log.SizeBytes)
            .First().Log;

    private static (bool HasContent, DateTime? Newest) Freshness(string path, LogFileKind kind)
    {
        if (!File.Exists(path)) return (false, null);

        switch (kind)
        {
            case LogFileKind.ChangeLog:
            {
                var entries = ChangeLogFile.ParseFile(path);
                return (entries.Count > 0, entries.Count == 0 ? null : entries.Max(e => e.Timestamp));
            }
            case LogFileKind.ErrorLog:
            {
                var entries = ErrLogFile.ParseFile(path);
                return (entries.Count > 0, entries.Count == 0 ? null : entries.Max(e => e.Timestamp));
            }
            default:
            {
                var info = new FileInfo(path);
                return (info.Length > 0, info.LastWriteTimeUtc);
            }
        }
    }

    private static bool InSupportFolder(string path) =>
        path.Replace('\\', '/').Contains("/support files/", StringComparison.OrdinalIgnoreCase);

    private static int Depth(string path) => path.Replace('\\', '/').Count(c => c == '/');

    /// <summary>The last two path segments, which is enough to tell two candidates apart.</summary>
    private static string Describe(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? string.Join('/', parts[^2..]) : path;
    }
}
