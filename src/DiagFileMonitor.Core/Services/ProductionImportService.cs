using DiagFileMonitor.Core.Data;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Production;
using Microsoft.EntityFrameworkCore;

namespace DiagFileMonitor.Core.Services;

public class ProductionImportResult
{
    public int FilesRead { get; init; }
    public int FilesSkippedAlreadyStored { get; init; }
    public int FilesSkippedNotProdLog { get; init; }
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
        int read = 0, skippedHeld = 0, skippedNotProd = 0, panelsStored = 0;

        // Same week in two folders: keep one. Ordering by week keeps the import in time order.
        var candidates = new Dictionary<(int Year, int Week), string>();

        foreach (var path in paths)
        {
            var week = ProdLogParser.WeekFromFileName(Path.GetFileName(path));
            if (week is null)
            {
                skippedNotProd++;
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

            await using var context = _contextFactory();

            var held = await context.ProductionLogFiles
                .FirstOrDefaultAsync(f => f.SerialNumber == serialNumber && f.Year == year && f.Week == weekNumber,
                    token);

            if (held is not null)
            {
                if (!replaceExisting)
                {
                    skippedHeld++;
                    continue;
                }

                context.ProductionLogFiles.Remove(held);
                await context.SaveChangesAsync(token);
            }

            var parsed = ProdLogParser.ParseFile(path);
            var classified = new PanelClassifier(_options).Classify(parsed.Events);

            var fileNotes = new List<string>();
            if (parsed.HadNullPadding) fileNotes.Add("contained NUL padding, stripped before parsing");
            if (parsed.MalformedLines > 0) fileNotes.Add($"{parsed.MalformedLines} unreadable line(s) skipped");
            if (parsed.UnknownEventNames.Count > 0)
                fileNotes.Add("unrecognised event(s): "
                              + string.Join(", ", parsed.UnknownEventNames.Select(u => $"{u.Key} x{u.Value}")));
            if (classified.UnexpectedFieldCounts > 0)
                fileNotes.Add($"{classified.UnexpectedFieldCounts} PanelAssembled row(s) had too few fields");

            var record = new ProductionLogFile
            {
                SerialNumber = serialNumber,
                FileName = Path.GetFileName(path),
                Year = year,
                Week = weekNumber,
                ImportedAtUtc = DateTime.UtcNow,
                LinesRead = parsed.LinesRead,
                ConsecutiveDuplicates = parsed.ConsecutiveDuplicates,
                MalformedLines = parsed.MalformedLines,
                PanelsStored = classified.Panels.Count,
                Notes = fileNotes.Count > 0 ? string.Join("; ", fileNotes) : null,
                Panels = classified.Panels.Select(p => new ProductionPanel
                {
                    SerialNumber = serialNumber,
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
                }).ToList()
            };

            context.ProductionLogFiles.Add(record);
            await context.SaveChangesAsync(token);

            read++;
            panelsStored += record.PanelsStored;
            foreach (var note in fileNotes) notes.Add($"{record.FileName}: {note}");
        }

        if (skippedNotProd > 0)
            notes.Add($"{skippedNotProd} file(s) were not named like a ProdLogV2 week and were left alone.");

        return new ProductionImportResult
        {
            FilesRead = read,
            FilesSkippedAlreadyStored = skippedHeld,
            FilesSkippedNotProdLog = skippedNotProd,
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
        int read = 0, skippedHeld = 0, skippedNotProd = 0, panels = 0;

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
            panels += result.PanelsStored;

            if (result.FilesRead > 0 || result.FilesSkippedAlreadyStored > 0) serials.Add(serial);
            foreach (var note in result.Notes) notes.Add($"{serial}: {note}");
        }

        return new ProductionImportResult
        {
            FilesRead = read,
            FilesSkippedAlreadyStored = skippedHeld,
            FilesSkippedNotProdLog = skippedNotProd,
            PanelsStored = panels,
            Serials = serials,
            Notes = notes
        };
    }

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
