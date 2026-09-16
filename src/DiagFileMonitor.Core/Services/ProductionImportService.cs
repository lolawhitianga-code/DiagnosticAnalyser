using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Production;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Services;

public class ProductionImportResult
{
    public int FilesRead { get; init; }
    public int FilesSkippedAlreadyStored { get; init; }
    public int FilesSkippedNotProdLog { get; init; }

    /// <summary>Files named the older ProdLog way, without the V2. Seen and not read.</summary>
    public int FilesInOlderFormat { get; init; }

    /// <summary>Weeks whose file held no events at all - a zero byte log.</summary>
    public int FilesEmpty { get; init; }
    public int PanelsStored { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>Which machines this import touched, for a sweep across several folders.</summary>
    public IReadOnlyList<string> Serials { get; init; } = Array.Empty<string>();

    public string Summary
    {
        get
        {
            if (FilesRead == 0 && FilesSkippedAlreadyStored == 0) return "Nothing new to import.";

            var machines = Serials.Count > 1 ? $" across {Serials.Count} machines" : string.Empty;
            var read = FilesRead == 0
                ? "Nothing new to read"
                : $"Read {FilesRead} week(s), stored {PanelsStored:N0} panel(s){machines}";

            return read + "."
                   + (FilesSkippedAlreadyStored > 0
                       ? $" Skipped {FilesSkippedAlreadyStored} week(s) already held."
                       : string.Empty);
        }
    }
}

/// <summary>
/// Reads ProdLogV2 weekly logs into the local database.
/// <para>
/// A week is identified by serial plus ISO week from the file name, not by path, because exports
/// carry a duplicate copy of recent weeks in a second folder. Re-importing a week already held is
/// skipped rather than doubled, unless it is asked for explicitly.
/// </para>
/// </summary>
public class ProductionImportService
{
    private readonly Func<DiagDbContext> _contextFactory;
    private readonly PanelClassifierOptions _options;

    public ProductionImportService(Func<DiagDbContext> contextFactory, PanelClassifierOptions? options = null)
    {
        _contextFactory = contextFactory;
        _options = options ?? new PanelClassifierOptions();
    }

    /// <summary>Reads every ProdLogV2 file under a folder, newest week last.</summary>
    public async Task<ProductionImportResult> ImportFolderAsync(
        string folder, string serialNumber, bool replaceExisting = false, CancellationToken token = default)
    {
        if (!Directory.Exists(folder))
            return new ProductionImportResult { Notes = new[] { $"Folder not found: {folder}" } };

        var files = Directory.EnumerateFiles(folder, "*.log", SearchOption.AllDirectories).ToList();
        return await ImportFilesAsync(files, serialNumber, replaceExisting, token);
    }

    public async Task<ProductionImportResult> ImportFilesAsync(
        IEnumerable<string> paths, string serialNumber, bool replaceExisting = false,
        CancellationToken token = default)
    {
        var notes = new List<string>();
        int read = 0, skippedHeld = 0, skippedNotProd = 0, panelsStored = 0, older = 0, empty = 0;

        // Same week in two folders: keep one. Ordering by week keeps the import in time order.
        var candidates = new Dictionary<(int Year, int Week), string>();

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var week = ProdLogParser.WeekFromFileName(name);

            if (week is null)
            {
                if (ProdLogParser.LooksLikeOlderProdLog(name)) older++;
                else skippedNotProd++;
                continue;
            }

            if (candidates.TryGetValue(week.Value, out var existing))
            {
                notes.Add($"Week {week.Value.Year}W{week.Value.Week:00} appears more than once; "
                          + $"read {Path.GetFileName(existing)} and ignored {Path.GetFileName(path)}.");
                continue;
            }

            candidates[week.Value] = path;
        }

        foreach (var ((year, weekNumber), path) in candidates.OrderBy(c => c.Key.Year).ThenBy(c => c.Key.Week))
        {
            token.ThrowIfCancellationRequested();

            var parsed = ProdLogParser.ParseFile(path);

            // A zero byte week is not a week with no production - it is a file with nothing in it.
            // Storing it would put an empty week in the history and read as a shutdown.
            if (parsed.Events.Count == 0)
            {
                empty++;
                continue;
            }

            var classified = new PanelClassifier(_options).Classify(parsed.Events);

            var stored = await StoreAsync(new StoreRequest
            {
                SerialNumber = serialNumber,
                FileName = Path.GetFileName(path),
                Year = year,
                Week = weekNumber,
                Source = ProductionSources.WeeklyLog,
                Parsed = parsed,
                Panels = classified.Panels,
                ClassifierNotes = classified.UnexpectedFieldCounts,
                ReplaceExisting = replaceExisting
            }, token);

            if (stored.FilesSkippedAlreadyStored > 0)
            {
                skippedHeld++;
                continue;
            }

            read++;
            panelsStored += stored.PanelsStored;
            notes.AddRange(stored.Notes);
        }

        if (empty > 0)
            notes.Add($"{empty} ProdLogV2 file(s) were empty and were not stored.");

        if (older > 0)
            notes.Add($"{older} file(s) are named the older way, ProdLog without the V2. They are not "
                      + "read - send one with data in it if those weeks matter.");

        if (skippedNotProd > 0)
            notes.Add($"{skippedNotProd} other .log file(s) were left alone - ShiftLogs and the like.");

        return new ProductionImportResult
        {
            FilesRead = read,
            FilesSkippedAlreadyStored = skippedHeld,
            FilesSkippedNotProdLog = skippedNotProd,
            FilesInOlderFormat = older,
            FilesEmpty = empty,
            PanelsStored = panelsStored,
            Serials = read > 0 || skippedHeld > 0 ? new[] { serialNumber } : Array.Empty<string>(),
            Notes = notes
        };
    }

    /// <summary>
    /// Reads a folder that holds one sub-folder per machine, taking each machine's serial from its
    /// own folder name.
    /// <para>
    /// This is how these exports usually arrive, and it is the only thing that says which machine a
    /// set of logs belongs to - ProdLogV2 carries no identity of its own. A sub-folder whose name
    /// does not contain a serial is left alone rather than filed under a guess.
    /// </para>
    /// </summary>
    public async Task<ProductionImportResult> ImportMachineFoldersAsync(
        string root, bool replaceExisting = false, CancellationToken token = default)
    {
        var folders = ProductionSerial.MachineFolders(root);

        if (folders.Count == 0)
        {
            return new ProductionImportResult
            {
                Notes = new[] { $"No sub-folder of {root} holds any ProdLogV2 files." }
            };
        }

        var notes = new List<string>();
        var serials = new List<string>();
        int read = 0, skippedHeld = 0, skippedNotProd = 0, panels = 0, older = 0, empty = 0;

        foreach (var (folder, serial) in folders)
        {
            if (serial is null)
            {
                notes.Add($"{Path.GetFileName(folder)}: no serial number in the folder name, so it "
                          + "was skipped. Rename it or import it on its own.");
                continue;
            }

            var result = await ImportFolderAsync(folder, serial, replaceExisting, token);

            read += result.FilesRead;
            skippedHeld += result.FilesSkippedAlreadyStored;
            skippedNotProd += result.FilesSkippedNotProdLog;
            older += result.FilesInOlderFormat;
            empty += result.FilesEmpty;
            panels += result.PanelsStored;

            if (result.FilesRead > 0 || result.FilesSkippedAlreadyStored > 0) serials.Add(serial);
            foreach (var note in result.Notes) notes.Add($"{serial}: {note}");
        }

        return new ProductionImportResult
        {
            FilesRead = read,
            FilesSkippedAlreadyStored = skippedHeld,
            FilesSkippedNotProdLog = skippedNotProd,
            FilesInOlderFormat = older,
            FilesEmpty = empty,
            PanelsStored = panels,
            Serials = serials,
            Notes = notes
        };
    }

    /// <summary>
    /// Reads the production report a diagnostic bundle carries as <c>Reports/LatestReport.txt</c>.
    /// <para>
    /// This is where the two halves meet. The weekly ProdLogV2 exports hold plenty of production
    /// data and no machine identity; a support bundle holds a few days of the same data and a
    /// Machine.xml that says exactly which machine it came from. So a bundle's report is filed
    /// under the serial the bundle already reported about itself.
    /// </para>
    /// <para>
    /// A bundle covers part of a week, and the same days may also arrive later in a full weekly
    /// export. Both are kept and the overlapping panels are de-duplicated, so neither source has
    /// to be preferred over the other.
    /// </para>
    /// </summary>
    public async Task<ProductionImportResult> ImportBundleReportAsync(
        string reportPath, string serialNumber, string bundleName = "", CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            return new ProductionImportResult { Notes = new[] { "No serial number, so there is nothing to file it under." } };

        if (!File.Exists(reportPath) || new FileInfo(reportPath).Length == 0)
            return new ProductionImportResult();

        if (!ProdLogParser.LooksLikeProductionContent(reportPath))
            return new ProductionImportResult();

        var parsed = ProdLogParser.ParseFile(reportPath);
        if (parsed.Events.Count == 0 || parsed.LastEvent is null) return new ProductionImportResult();

        // The report has no week in its name, so the events date it. The last one is the moment
        // the export was taken.
        var last = parsed.LastEvent.Value;
        var year = ISOWeek.GetYear(last);
        var week = ISOWeek.GetWeekOfYear(last);

        var classified = new PanelClassifier(_options).Classify(parsed.Events);

        return await StoreAsync(new StoreRequest
        {
            SerialNumber = serialNumber,
            FileName = string.IsNullOrWhiteSpace(bundleName)
                ? Path.GetFileName(reportPath)
                : $"{bundleName} ({Path.GetFileName(reportPath)})",
            Year = year,
            Week = week,
            Source = ProductionSources.SupportBundle,
            Parsed = parsed,
            Panels = classified.Panels,
            ClassifierNotes = classified.UnexpectedFieldCounts,
            ReplaceExisting = true
        }, token);
    }

    private class StoreRequest
    {
        public string SerialNumber = string.Empty;
        public string FileName = string.Empty;
        public int Year;
        public int Week;
        public string Source = ProductionSources.WeeklyLog;
        public ProdLogParseResult Parsed = new();
        public IReadOnlyList<PanelRecord> Panels = Array.Empty<PanelRecord>();
        public int ClassifierNotes;
        public bool ReplaceExisting;
    }

    /// <summary>
    /// Writes one file's panels, leaving out any already held for the same machine at the same
    /// moment. Two sources overlapping is normal, and a panel counted twice would inflate every
    /// figure built on it.
    /// </summary>
    private async Task<ProductionImportResult> StoreAsync(StoreRequest request, CancellationToken token)
    {
        await using var context = _contextFactory();

        var held = await context.ProductionLogFiles.FirstOrDefaultAsync(
            f => f.SerialNumber == request.SerialNumber && f.Year == request.Year
                 && f.Week == request.Week && f.Source == request.Source, token);

        if (held is not null)
        {
            if (!request.ReplaceExisting)
            {
                return new ProductionImportResult { FilesSkippedAlreadyStored = 1 };
            }

            context.ProductionLogFiles.Remove(held);
            await context.SaveChangesAsync(token);
        }

        var from = request.Panels.Count > 0 ? request.Panels.Min(p => p.EndedAt) : (DateTime?)null;
        var to = request.Panels.Count > 0 ? request.Panels.Max(p => p.EndedAt) : (DateTime?)null;

        // Only the window this file covers has to be checked, which keeps the lookup small.
        var alreadyHeld = from is null
            ? new HashSet<string>()
            : (await context.ProductionPanels.AsNoTracking()
                    .Where(p => p.SerialNumber == request.SerialNumber
                                && p.EndedAt >= from && p.EndedAt <= to)
                    .Select(p => new { p.EndedAt, p.Name, p.Outcome })
                    .ToListAsync(token))
                .Select(p => PanelKey(p.EndedAt, p.Name, p.Outcome))
                .ToHashSet();

        var fresh = request.Panels
            .Where(p => !alreadyHeld.Contains(PanelKey(p.EndedAt, p.Name, p.Outcome.ToString())))
            .ToList();

        var fileNotes = new List<string>();
        if (request.Parsed.HadNullPadding) fileNotes.Add("contained NUL padding, stripped before parsing");
        if (request.Parsed.MalformedLines > 0) fileNotes.Add($"{request.Parsed.MalformedLines} unreadable line(s) skipped");
        if (request.Parsed.UnknownEventNames.Count > 0)
            fileNotes.Add("unrecognised event(s): "
                          + string.Join(", ", request.Parsed.UnknownEventNames.Select(u => $"{u.Key} x{u.Value}")));
        if (request.ClassifierNotes > 0)
            fileNotes.Add($"{request.ClassifierNotes} PanelAssembled row(s) had too few fields");
        if (fresh.Count < request.Panels.Count)
            fileNotes.Add($"{request.Panels.Count - fresh.Count} panel(s) were already held from another source");

        var record = new ProductionLogFile
        {
            SerialNumber = request.SerialNumber,
            FileName = request.FileName,
            Year = request.Year,
            Week = request.Week,
            Source = request.Source,
            ImportedAtUtc = DateTime.UtcNow,
            LinesRead = request.Parsed.LinesRead,
            ConsecutiveDuplicates = request.Parsed.ConsecutiveDuplicates,
            MalformedLines = request.Parsed.MalformedLines,
            PanelsStored = fresh.Count,
            PanelsSkippedAsDuplicate = request.Panels.Count - fresh.Count,
            CoversFromUtc = from,
            CoversToUtc = to,
            Notes = fileNotes.Count > 0 ? string.Join("; ", fileNotes) : null,
            Panels = fresh.Select(p => ToRow(p, request.SerialNumber)).ToList()
        };

        context.ProductionLogFiles.Add(record);
        await context.SaveChangesAsync(token);

        return new ProductionImportResult
        {
            FilesRead = 1,
            PanelsStored = fresh.Count,
            Serials = new[] { request.SerialNumber },
            Notes = fileNotes.Select(n => $"{record.FileName}: {n}").ToList()
        };
    }

    /// <summary>A panel is the same panel when the same machine closed the same name at the same
    /// instant. Two sources covering one day produce identical rows.</summary>
    private static string PanelKey(DateTime endedAt, string name, string outcome) =>
        $"{endedAt:O}|{name}|{outcome}";

    private static ProductionPanel ToRow(PanelRecord p, string serial) => new()
    {
        SerialNumber = serial,
        Name = p.Name,
        EndedAt = p.EndedAt,
        StartedAt = p.StartedAt,
        Outcome = p.Outcome.ToString(),
        FastenerCount = p.FastenerCount,
        MembersAssembled = p.MembersAssembled,
        Cube = p.Cube,
        Lineal = p.Lineal,
        BuildMinutes = p.BuildMinutes,
        IdleMinutes = p.IdleMinutes,
        Junctions = p.Junctions,
        BuildTimeImplausible = p.BuildTimeImplausible
    };

    /// <summary>Panels back out of the database, as the analyser wants them.</summary>
    public async Task<IReadOnlyList<PanelRecord>> LoadPanelsAsync(
        string serialNumber, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken token = default)
    {
        await using var context = _contextFactory();

        var query = context.ProductionPanels.AsNoTracking()
            .Where(p => p.SerialNumber == serialNumber);

        if (fromUtc is { } from) query = query.Where(p => p.EndedAt >= from);
        if (toUtc is { } to) query = query.Where(p => p.EndedAt < to);

        var rows = await query.OrderBy(p => p.EndedAt).ToListAsync(token);

        return rows.Select(r => new PanelRecord
        {
            Name = r.Name,
            EndedAt = r.EndedAt,
            StartedAt = r.StartedAt,
            Outcome = Enum.TryParse<PanelOutcome>(r.Outcome, out var outcome) ? outcome : PanelOutcome.Completed,
            FastenerCount = r.FastenerCount,
            MembersAssembled = r.MembersAssembled,
            Cube = r.Cube,
            Lineal = r.Lineal,
            BuildMinutes = r.BuildMinutes,
            IdleMinutes = r.IdleMinutes,
            Junctions = r.Junctions,
            BuildTimeImplausible = r.BuildTimeImplausible
        }).ToList();
    }

    /// <summary>Which machines have production data stored, and how much.</summary>
    public async Task<IReadOnlyList<(string SerialNumber, int Weeks, int Panels)>> StoredMachinesAsync(
        CancellationToken token = default)
    {
        await using var context = _contextFactory();

        var files = await context.ProductionLogFiles.AsNoTracking()
            .GroupBy(f => f.SerialNumber)
            .Select(g => new { Serial = g.Key, Weeks = g.Count(), Panels = g.Sum(f => f.PanelsStored) })
            .ToListAsync(token);

        return files
            .Select(f => (f.Serial, f.Weeks, f.Panels))
            .OrderBy(f => f.Serial, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
