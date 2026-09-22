using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// Change.log moved in some SDN versions and the old copy stays behind. M20771 carried both:
/// the deeper Logs/Support copy stopped in March, the root copy ran to the day of the export.
/// </summary>
public sealed class ChangeLogSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "changelogsel", Guid.NewGuid().ToString("N"));

    public ChangeLogSelectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ExtractedLogFile Write(string relative, params string[] lines)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return new ExtractedLogFile
        {
            FileName = Path.GetFileName(path), FullPath = path, Kind = LogFileKind.ChangeLog,
            SizeBytes = new FileInfo(path).Length
        };
    }

    private static DiagnosticFile Bundle(params ExtractedLogFile[] logs)
    {
        var bundle = new DiagnosticFile { OriginalFileName = "M20771.szip", SerialNumber = "M20771" };
        foreach (var log in logs) bundle.LogFiles.Add(log);
        return bundle;
    }

    [Fact]
    public void The_copy_with_the_newest_entry_wins_even_when_shallower()
    {
        var old = Write("Logs/Support/Logs/Change.log",
            "16/01/2026 2:32:46 pm, Spida, WallExtruder, RWEPanelHeightGap, 5, 8",
            "3/03/2026 10:39:44 am, Spida, WallExtruder, SingleNailSidewaysStuds, True, False");
        var live = Write("Change.log",
            "3/03/2026 10:39:44 am, Spida, WallExtruder, SingleNailSidewaysStuds, True, False",
            "22/09/2026 8:24:46 am, Spida, FixedSidePuller, HomePosition, 5901, 5903");

        var notes = new List<string>();
        Assert.Equal(live.FullPath, BundleLogs.PathOf(Bundle(old, live), LogFileKind.ChangeLog, notes));
        Assert.Single(notes);

        var logs = BundleLogs.Read(Bundle(old, live));
        Assert.Contains(logs.ChangeLog, e => e.Setting == "HomePosition" && e.Category == "FixedSidePuller");

        Assert.Equal(live.FullPath, DiagnosticFileSummary.FromEntity(Bundle(old, live)).ChangeLogPath);
    }

    [Fact]
    public void An_unreadable_copy_loses_to_one_with_entries()
    {
        var empty = Write("Logs/Support/Logs/Change.log");
        var live = Write("Change.log", "22/09/2026 8:24:46 am, Spida, FixedSidePuller, HomePosition, 5901, 5903");

        Assert.Equal(live.FullPath, BundleLogs.PathOf(Bundle(empty, live), LogFileKind.ChangeLog));
    }

    [Fact]
    public void One_copy_is_simply_read()
    {
        var only = Write("Change.log", "22/09/2026 8:24:46 am, Spida, FixedSidePuller, HomePosition, 5901, 5903");

        Assert.Equal(only.FullPath, BundleLogs.PathOf(Bundle(only), LogFileKind.ChangeLog));
    }
}
