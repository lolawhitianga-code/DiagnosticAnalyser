using System.IO.Compression;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.Core.Services;

/// <summary>Unpacks one diagnostic zip, reads machine.xml, indexes the extracted files, and persists the result.</summary>
public class DiagFileProcessor
{
    private readonly string _extractRootPath;
    private readonly DiagFileRepository _repository;
    private readonly bool _fileNameTimesAreUtc;

    private static readonly Dictionary<string, LogFileKind> KnownLogFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Real Spida exports use Logs\MachineLog.txt, Logs\ErrLog.txt and Logs\Change.log.
        // The lowercase spellings are kept so older or hand-made bundles still line up.
        ["machinelog.txt"] = LogFileKind.MachineLog,
        ["errlog.txt"] = LogFileKind.ErrorLog,
        ["errorlog.txt"] = LogFileKind.ErrorLog,
        ["change.log"] = LogFileKind.ChangeLog,
        ["changelog.txt"] = LogFileKind.ChangeLog,
        ["supportinfo.txt"] = LogFileKind.SupportInfo
    };

    private readonly ProductionImportService? _production;
    private readonly SignalCatalogueService? _signals;

    public DiagFileProcessor(string extractRootPath, DiagFileRepository repository,
        bool fileNameTimesAreUtc = true, ProductionImportService? production = null,
        SignalCatalogueService? signals = null)
    {
        _extractRootPath = extractRootPath;
        _repository = repository;
        _fileNameTimesAreUtc = fileNameTimesAreUtc;
        _production = production;
        _signals = signals;
        Directory.CreateDirectory(_extractRootPath);
    }

    /// <summary>Whether this bundle has been imported before, so a re-scan can skip it.</summary>
    public async Task<bool> IsAlreadyStoredAsync(string zipPath)
    {
        try
        {
            var info = new FileInfo(zipPath);
            if (!info.Exists) return false;

            return await _repository.ExistsAsync(
                info.Name, info.Length, DiagFileNameDate.ArrivedUtc(zipPath, _fileNameTimesAreUtc));
        }
        catch (IOException)
        {
            // If we cannot tell, let it through rather than silently dropping a bundle.
            return false;
        }
    }

    public async Task<DiagnosticFile> ProcessAsync(string zipPath, CancellationToken token = default)
    {
        var fileInfo = new FileInfo(zipPath);
        var diagFile = new DiagnosticFile
        {
            OriginalFileName = fileInfo.Name,
            SourcePath = zipPath,
            FileSizeBytes = fileInfo.Length,
            // The name carries when the machine produced the bundle; the file date only
            // says when it was last copied about.
            ArrivedAtUtc = DiagFileNameDate.ArrivedUtc(zipPath, _fileNameTimesAreUtc),
            Status = ProcessingStatus.Pending
        };

        try
        {
            var extractDir = CreateUniqueExtractDir(fileInfo.Name);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            diagFile.ExtractedPath = extractDir;

            var machineXmlPath = FindByName(extractDir, "machine.xml");

            if (machineXmlPath is not null)
            {
                var info = MachineXmlParser.Parse(machineXmlPath);
                diagFile.MachineType = info.MachineType;
                diagFile.MachineName = info.MachineName;
                diagFile.SerialNumber = info.SerialNumber;
                diagFile.Customer = info.Customer;
                diagFile.SiteLocation = info.SiteLocation;
                diagFile.SoftwareName = info.SoftwareName;
                diagFile.Version = info.Version;
            }
            else
            {
                SimpleLogger.Info($"No machine.xml found in '{zipPath}'.");
            }

            var supportInfoPath = FindByName(extractDir, "supportinfo.txt");

            if (supportInfoPath is not null)
            {
                var support = SupportInfoParser.ParseFile(supportInfoPath);
                diagFile.SupportPanel = support.Panel;
                diagFile.SupportMembers = support.Members;
                diagFile.SupportIssue = support.Issue;
            }

            foreach (var extractedFile in Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(extractedFile);
                var kind = KnownLogFiles.TryGetValue(name, out var knownKind) ? knownKind : LogFileKind.Other;

                diagFile.LogFiles.Add(new ExtractedLogFile
                {
                    FileName = name,
                    FullPath = extractedFile,
                    SizeBytes = new FileInfo(extractedFile).Length,
                    Kind = kind
                });
            }

            if (string.IsNullOrWhiteSpace(diagFile.SerialNumber))
            {
                diagFile.Status = ProcessingStatus.Error;
                diagFile.ErrorMessage = "machine.xml was missing or did not contain a serial number.";
            }
            else
            {
                diagFile.Status = ProcessingStatus.Processed;
            }
        }
        catch (Exception ex)
        {
            diagFile.Status = ProcessingStatus.Error;
            diagFile.ErrorMessage = ex.Message;
            SimpleLogger.Error($"Error processing '{zipPath}'", ex);
        }
        finally
        {
            diagFile.ProcessedAtUtc = DateTime.UtcNow;
        }

        await _repository.AddAsync(diagFile);

        // A bundle carries its own production report, and unlike the weekly exports it also says
        // which machine it came from. That pairing is the only thing that files production data
        // against a serial without somebody typing one in.
        await ImportProductionReportAsync(diagFile, token);
        await LearnSignalsAsync(diagFile, token);

        return diagFile;
    }

    /// <summary>
    /// Finds one file by name anywhere under the extract folder, ignoring case.
    /// <para>
    /// The export writes Machine.xml and SupportInfo.txt in mixed case. A filename pattern passed
    /// to <see cref="Directory.EnumerateFiles(string,string,SearchOption)"/> matches case
    /// insensitively on Windows but not on Linux, so matching here rather than in the glob keeps
    /// the behaviour the same wherever it runs - including the Linux test box.
    /// </para>
    /// </summary>
    /// <summary>
    /// Stores the production data inside a bundle against the machine the bundle reported itself
    /// to be. Anything going wrong here is noted and swallowed: production figures are a bonus,
    /// and never a reason to fail the diagnostic import the user actually asked for.
    /// </summary>
    private async Task ImportProductionReportAsync(DiagnosticFile diagFile, CancellationToken token)
    {
        if (_production is null) return;
        if (diagFile.Status != ProcessingStatus.Processed) return;
        if (string.IsNullOrWhiteSpace(diagFile.SerialNumber)) return;
        if (diagFile.ExtractedPath is null) return;

        try
        {
            var report = FindByName(diagFile.ExtractedPath, "latestreport.txt");
            if (report is null) return;

            var result = await _production.ImportBundleReportAsync(
                report, diagFile.SerialNumber.Trim(), diagFile.OriginalFileName, token);

            if (result.PanelsStored > 0)
            {
                SimpleLogger.Info($"Stored {result.PanelsStored} production panel(s) from "
                                  + $"'{diagFile.OriginalFileName}' against {diagFile.SerialNumber}.");
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Error($"Could not read the production report in '{diagFile.OriginalFileName}'", ex);
        }
    }

    /// <summary>
    /// Folds this bundle's machine log into what is known about the machine's I/O.
    /// <para>
    /// Nothing else carries a list of a machine's inputs and outputs, so it is learned by
    /// watching. Like the production import, anything going wrong here is noted and swallowed -
    /// it must never cost the user the diagnostic import they actually asked for.
    /// </para>
    /// </summary>
    private async Task LearnSignalsAsync(DiagnosticFile diagFile, CancellationToken token)
    {
        if (_signals is null) return;
        if (diagFile.Status != ProcessingStatus.Processed) return;
        if (string.IsNullOrWhiteSpace(diagFile.SerialNumber)) return;
        if (diagFile.ExtractedPath is null) return;

        try
        {
            var log = FindByName(diagFile.ExtractedPath, "machinelog.txt");
            if (log is null) return;

            var result = await _signals.RecordAsync(
                diagFile.SerialNumber.Trim(), diagFile.MachineType ?? string.Empty,
                IoTimeline.FromFile(log), token);

            if (result.PointsNew > 0)
            {
                SimpleLogger.Info($"Learned {result.PointsNew} new I/O point(s) for "
                                  + $"{diagFile.SerialNumber} from '{diagFile.OriginalFileName}'.");
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Error($"Could not read the I/O out of '{diagFile.OriginalFileName}'", ex);
        }
    }

    private static string? FindByName(string root, string fileName) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .FirstOrDefault(p => Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase));

    private string CreateUniqueExtractDir(string zipFileName)
    {
        var baseName = SanitizeForPath(Path.GetFileNameWithoutExtension(zipFileName));
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");

        // Two bundles can land inside the same millisecond, so keep trying until we get a new folder.
        var dir = Path.Combine(_extractRootPath, $"{stamp}_{baseName}");
        var attempt = 1;
        while (Directory.Exists(dir))
        {
            dir = Path.Combine(_extractRootPath, $"{stamp}_{baseName}_{attempt++}");
        }

        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SanitizeForPath(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
