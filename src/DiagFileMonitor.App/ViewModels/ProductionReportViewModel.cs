using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Production;
using DiagFileMonitor.Core.Reports;
using DiagFileMonitor.Core.Services;

namespace DiagFileMonitor.App.ViewModels;

/// <summary>
/// Reads ProdLogV2 weekly logs into the local database and builds a production report from what
/// is stored.
/// <para>
/// Importing and reporting are separate on purpose: the logs are read once and kept, so a report
/// covering a year does not mean re-reading a year of files every time.
/// </para>
/// </summary>
public partial class ProductionReportViewModel : ObservableObject
{
    private readonly ProductionImportService _import;
    private readonly string _outputFolder;

    [ObservableProperty] private string _serialNumber = string.Empty;
    [ObservableProperty] private string _machineName = string.Empty;
    [ObservableProperty] private string _site = string.Empty;
    [ObservableProperty] private string _logFolder = string.Empty;

    [ObservableProperty] private int _shiftModelIndex;
    [ObservableProperty] private bool _replaceExisting;

    /// <summary>Treat the chosen folder as a parent, with one sub-folder per machine.</summary>
    [ObservableProperty] private bool _eachSubfolderIsAMachine;

    /// <summary>What the folder name says the machine is, shown so the guess is visible.</summary>
    [ObservableProperty] private string _folderHint = string.Empty;

    [ObservableProperty] private string _storedSummary = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _createdPath = string.Empty;

    [ObservableProperty] private string _shiftStart = "07:00";
    [ObservableProperty] private string _shiftEnd = "17:00";
    [ObservableProperty] private string _breaks = "10:00-10:15, 12:30-13:00, 14:30-14:45";
    [ObservableProperty] private string _stopThresholdMinutes = "20";

    public List<string> ShiftModelOptions { get; } = new()
    {
        "Single day shift, 07:00-17:00 with three breaks",
        "Round the clock, six breaks (Carters Auckland)",
        "My own times (set below)",
        "Ignore shift - do not report availability"
    };

    /// <summary>The boxes only matter for the hand-set model.</summary>
    public bool ShiftIsEditable => ShiftModelIndex == 2;

    /// <summary>What the chosen model comes to, in words, so a mistake is visible before building.</summary>
    public string ShiftSummary => Shift.Ignored
        ? "Availability will not be reported. Panels, cube and lineal metres are measured either way."
        : $"{Shift.PlannedMinutesPerDay / 60:F1} rostered hours a day after "
          + $"{Shift.Breaks.Count} break(s), stop threshold {Shift.UnplannedStopMinutes:F0} min.";

    public bool HasCreatedReport => CreatedPath.Length > 0;

    public ProductionReportViewModel(ProductionImportService import, string outputFolder,
        IReadOnlyList<string>? serials = null)
    {
        _import = import;
        _outputFolder = outputFolder;

        // A production report is for one machine at a time, so only the first carries over.
        if (serials is { Count: > 0 }) SerialNumber = serials[0];
    }

    partial void OnCreatedPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasCreatedReport));
        OpenReportCommand.NotifyCanExecuteChanged();
    }

    partial void OnSerialNumberChanged(string value)
    {
        ImportCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged();
    }

    partial void OnLogFolderChanged(string value)
    {
        ImportCommand.NotifyCanExecuteChanged();
        DescribeFolder();
    }

    partial void OnEachSubfolderIsAMachineChanged(bool value)
    {
        ImportCommand.NotifyCanExecuteChanged();
        DescribeFolder();
    }

    /// <summary>
    /// Opens a folder picker and reads the machine's serial out of the folder name, because the
    /// production logs themselves carry no machine identity at all.
    /// </summary>
    [RelayCommand]
    private void BrowseForFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folder holding the ProdLogV2 files",
            InitialDirectory = Directory.Exists(LogFolder.Trim()) ? LogFolder.Trim() : null
        };

        if (dialog.ShowDialog() != true) return;

        LogFolder = dialog.FolderName;

        // Only fill the serial in where the user has not typed one, so a picker never overwrites
        // what somebody deliberately entered.
        if (SerialNumber.Trim().Length == 0 && ProductionSerial.FromPath(LogFolder) is { } found)
        {
            SerialNumber = found;
            StatusMessage = $"Serial {found} taken from the folder name. Change it if that is wrong.";
        }
    }

    /// <summary>Says what the chosen folder looks like before anything is read from it.</summary>
    private void DescribeFolder()
    {
        var folder = LogFolder.Trim();

        if (folder.Length == 0)
        {
            FolderHint = string.Empty;
            return;
        }

        if (!Directory.Exists(folder))
        {
            FolderHint = "That folder does not exist.";
            return;
        }

        if (EachSubfolderIsAMachine)
        {
            var machines = ProductionSerial.MachineFolders(folder);
            var named = machines.Where(m => m.Serial is not null).ToList();

            FolderHint = machines.Count == 0
                ? "No sub-folder here holds any ProdLogV2 files."
                : $"{machines.Count} sub-folder(s) with production logs, {named.Count} with a serial "
                  + $"in the name: {string.Join(", ", named.Select(m => m.Serial).Take(6))}"
                  + (named.Count > 6 ? ", ..." : string.Empty);

            return;
        }

        FolderHint = ProductionSerial.HoldsProductionLogs(folder)
            ? "ProdLogV2 files found in this folder."
            : "No ProdLogV2 files in this folder or below it.";
    }

    private ShiftModel Shift => ShiftModelIndex switch
    {
        1 => ShiftModel.CartersAucklandRoundTheClock,
        2 => HandSetShift(),
        3 => ShiftModel.NoShift,
        _ => ShiftModel.SingleDayShift
    };

    /// <summary>
    /// The roster typed into the boxes. Anything unreadable falls back to the day shift's value
    /// rather than refusing to build the report - the summary line above shows what was understood.
    /// </summary>
    private ShiftModel HandSetShift()
    {
        var fallback = ShiftModel.SingleDayShift;

        return new ShiftModel
        {
            Name = $"Set by hand: {ShiftStart.Trim()} to {ShiftEnd.Trim()}",
            ShiftStart = TimeOnly.TryParse(ShiftStart.Trim(), out var from) ? from : fallback.ShiftStart,
            ShiftEnd = TimeOnly.TryParse(ShiftEnd.Trim(), out var to) ? to : fallback.ShiftEnd,
            Breaks = ShiftModel.ParseBreaks(Breaks),
            UnplannedStopMinutes = double.TryParse(StopThresholdMinutes.Trim(), out var minutes) && minutes > 0
                ? minutes
                : fallback.UnplannedStopMinutes
        };
    }

    partial void OnShiftModelIndexChanged(int value) => ShiftChanged();
    partial void OnShiftStartChanged(string value) => ShiftChanged();
    partial void OnShiftEndChanged(string value) => ShiftChanged();
    partial void OnBreaksChanged(string value) => ShiftChanged();
    partial void OnStopThresholdMinutesChanged(string value) => ShiftChanged();

    private void ShiftChanged()
    {
        OnPropertyChanged(nameof(ShiftIsEditable));
        OnPropertyChanged(nameof(ShiftSummary));
    }

    /// <summary>
    /// A sweep across sub-folders takes each machine's serial from its own folder name, so the
    /// serial box is not needed for it.
    /// </summary>
    private bool CanImport() =>
        !IsBusy
        && LogFolder.Trim().Length > 0
        && (EachSubfolderIsAMachine || SerialNumber.Trim().Length > 0);

    private bool CanBuild() => !IsBusy && SerialNumber.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        IsBusy = true;
        ImportCommand.NotifyCanExecuteChanged();
        StatusMessage = "Reading the production logs...";

        try
        {
            var result = EachSubfolderIsAMachine
                ? await _import.ImportMachineFoldersAsync(LogFolder.Trim(), ReplaceExisting)
                : await _import.ImportFolderAsync(LogFolder.Trim(), SerialNumber.Trim(), ReplaceExisting);

            // A sweep can pull in several machines; leave the box on one of them so a report can
            // be built straight away.
            if (EachSubfolderIsAMachine && result.Serials.Count > 0 && SerialNumber.Trim().Length == 0)
            {
                SerialNumber = result.Serials[0];
            }

            StatusMessage = result.Summary;
            foreach (var note in result.Notes) StatusMessage += Environment.NewLine + note;

            await RefreshStoredAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not read the logs: {ex.Message}";
            SimpleLogger.Error("Could not import production logs", ex);
        }
        finally
        {
            IsBusy = false;
            ImportCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task RefreshStoredAsync()
    {
        try
        {
            var machines = await _import.StoredMachinesAsync();

            StoredSummary = machines.Count == 0
                ? "No production logs stored yet."
                : string.Join(Environment.NewLine,
                    machines.Select(m => $"{m.SerialNumber}: {m.Weeks} week(s), {m.Panels:N0} panel(s)"));
        }
        catch (Exception ex)
        {
            StoredSummary = $"Could not read what is stored: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        IsBusy = true;
        BuildCommand.NotifyCanExecuteChanged();
        StatusMessage = "Building the report...";
        CreatedPath = string.Empty;

        try
        {
            var serial = SerialNumber.Trim();
            var panels = await _import.LoadPanelsAsync(serial);

            if (panels.Count == 0)
            {
                StatusMessage = $"Nothing stored for {serial}. Import its production logs first.";
                return;
            }

            var summary = ProductionAnalyser.Summarise(panels, Shift, serial, Site.Trim());
            var model = ProductionReport.Build(summary, MachineName.Trim(), panels: panels);
            var html = new ReportHtmlRenderer().Render(model);

            Directory.CreateDirectory(_outputFolder);
            var path = Path.Combine(_outputFolder,
                $"production-{Slug(serial)}-{summary.To:yyyy-MM-dd}.html");

            await File.WriteAllTextAsync(path, html);
            CreatedPath = path;

            StatusMessage = $"{summary.PanelsCompleted:N0} panel(s) over {summary.DaysWithOutput} "
                            + $"production day(s). Saved as {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not build the report: {ex.Message}";
            SimpleLogger.Error("Could not build the production report", ex);
        }
        finally
        {
            IsBusy = false;
            BuildCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(HasCreatedReport))]
    private void OpenReport()
    {
        try
        {
            Process.Start(new ProcessStartInfo(CreatedPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open it: {ex.Message}";
        }
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();

        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
