using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.Core.Tests;

/// <summary>
/// Logs moved between SDN versions and the old copies stay behind. M20771 carried two
/// Change.logs: the deeper Logs/Support copy stopped in March, the root copy ran to the day of
/// the export. The freshest copy wins, wherever it sits.
/// </summary>
public sealed class LogSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "logselection", Guid.NewGuid().ToString("N"));

    public LogSelectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ExtractedLogFile Write(string relative, params string[] lines) =>
        Write(relative, LogFileKind.ChangeLog, null, lines);

    /// <param name="written">The file time the zip carried, which extraction keeps.</param>
    private ExtractedLogFile Write(string relative, LogFileKind kind, DateTime? written, params string[] lines)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        if (written is { } when) File.SetLastWriteTimeUtc(path, when);
        return new ExtractedLogFile
        {
            FileName = Path.GetFileName(path), FullPath = path, Kind = kind,
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

    [Fact]
    public void Machine_log_lines_have_no_date_so_the_newest_file_wins()
    {
        var old = Write("Logs/Support/Logs/MachineLog.txt", LogFileKind.MachineLog, new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            "11:03:42.6545741,  Other, Machine Model,  RakingWallExtruderV3DG");
        var live = Write("Logs/MachineLog.txt", LogFileKind.MachineLog, new DateTime(2026, 9, 21, 20, 0, 0, DateTimeKind.Utc),
            "06:06:30.6378369,  Other, Machine Model,  RakingWallExtruderV3DG");

        Assert.Equal(live.FullPath, BundleLogs.PathOf(Bundle(old, live), LogFileKind.MachineLog));
        Assert.Equal(live.FullPath, DiagnosticFileSummary.FromEntity(Bundle(old, live)).MachineLogPath);
    }

    [Fact]
    public void An_empty_machine_log_never_wins_on_being_newer()
    {
        var real = Write("Logs/Support/Logs/MachineLog.txt", LogFileKind.MachineLog, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            "11:03:42.6545741,  Other, Machine Model,  RakingWallExtruderV3DG");
        var empty = Write("MachineLog.txt", LogFileKind.MachineLog, new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(real.FullPath, BundleLogs.PathOf(Bundle(real, empty), LogFileKind.MachineLog));
    }

    [Fact]
    public void Err_log_copy_with_the_newest_entry_wins()
    {
        var old = Write("Logs/Support/Logs/ErrLog.txt", LogFileKind.ErrorLog, null,
            "Date/Time: 9/07/2019 4:58:35 PM",
            "===========================================================================================",
            "Title: SDN",
            "Message: Object reference not set to an instance of an object.");
        var live = Write("ErrLog.txt", LogFileKind.ErrorLog, null,
            "Date/Time: 21/09/2026 4:58:35 PM",
            "===========================================================================================",
            "Title: SDN",
            "Message: Object reference not set to an instance of an object.");

        Assert.Equal(live.FullPath, BundleLogs.PathOf(Bundle(old, live), LogFileKind.ErrorLog));
        Assert.Equal(live.FullPath, DiagnosticFileSummary.FromEntity(Bundle(old, live)).ErrorLogPath);
    }
}
