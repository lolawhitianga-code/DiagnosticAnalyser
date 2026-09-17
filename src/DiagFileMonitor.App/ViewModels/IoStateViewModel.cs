using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiagFileMonitor.Core.Models;
using DiagFileMonitor.Core.Services;
using DiagFileMonitor.Core.SpidaLogs;

namespace DiagFileMonitor.App.ViewModels;

/// <summary>One line of the machine log, as the grid shows it.</summary>
public record LogLineRow(int LineNumber, string Time, string Category, string Tag, string Description)
{
    public bool IsChange => Category is "InputChange" or "OutputChange";
}

/// <summary>One I/O point and what it was doing at the chosen moment.</summary>
public record SignalRow(string Name, string Address, string State, string Since,
    int Moves, int MovesInFile, bool On, bool ReadBackwards)
{
    /// <summary>
    /// <see cref="Moves"/> is how often the point has moved by the moment being looked at, which
    /// is the useful half - a clamp on its fortieth move of the shift is a different story from
    /// one on its first. <see cref="MovesInFile"/> is the whole file, for scale.
    /// </summary>
    public string Note => ReadBackwards
        ? "nothing had touched it yet - read backwards from its next change"
        : string.Empty;
}

/// <summary>
/// Pick any moment in a MachineLog and see what every input and output was doing.
/// <para>
/// The list of points is built from the log itself, because nothing else carries it - Machine.xml
/// has no I/O map. That means a point only appears once it has moved at least once in the file.
/// Where a point had not moved yet at the chosen moment its state is read backwards from its next
/// change and the row says so, rather than quietly showing it off.
/// </para>
/// </summary>
public partial class IoStateViewModel : ObservableObject
{
    private readonly SignalCatalogueService? _catalogue;
    private readonly string _serialNumber;

    private IoTimeline _timeline = IoTimeline.Build(Array.Empty<MachineLogEntry>());
    private IReadOnlyList<LogLineRow> _allLines = Array.Empty<LogLineRow>();
    private IReadOnlyList<MachineSignal> _quietPoints = Array.Empty<MachineSignal>();

    [ObservableProperty] private string _logPath = string.Empty;
    [ObservableProperty] private string _heading = "No log loaded.";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _momentText = string.Empty;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _onlyWhatIsOn;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty] private IReadOnlyList<LogLineRow> _lines = Array.Empty<LogLineRow>();
    [ObservableProperty] private IReadOnlyList<SignalRow> _outputs = Array.Empty<SignalRow>();
    [ObservableProperty] private IReadOnlyList<SignalRow> _inputs = Array.Empty<SignalRow>();

    [ObservableProperty] private LogLineRow? _selectedLine;

    public IoStateViewModel(string? machineLogPath = null, string? heading = null,
        string serialNumber = "", SignalCatalogueService? catalogue = null)
    {
        _serialNumber = serialNumber;
        _catalogue = catalogue;

        if (heading is { Length: > 0 }) Heading = heading;
        if (machineLogPath is { Length: > 0 } && File.Exists(machineLogPath)) Load(machineLogPath);
    }

    /// <summary>
    /// Asks the catalogue what else this machine is known to have. A single log only shows the
    /// points that moved in it, and a clamp that moves in every other bundle and sits still in
    /// this one is usually the thing worth looking at.
    /// </summary>
    public async Task LoadCatalogueAsync()
    {
        if (_catalogue is null || _serialNumber.Trim().Length == 0) return;

        try
        {
            _quietPoints = await _catalogue.NotInThisLogAsync(_serialNumber, _timeline);
            Notes = BuildNotes();
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Could not read the known I/O for this machine", ex);
        }
    }

    partial void OnSelectedLineChanged(LogLineRow? value)
    {
        // Loading a file with nothing readable in it has to empty the grids, or the last file's
        // reading sits there looking like this one's.
        if (value is null)
        {
            Outputs = Array.Empty<SignalRow>();
            Inputs = Array.Empty<SignalRow>();
            Summary = string.Empty;
            return;
        }

        // Typing a time is one way in and clicking a line is the other; both end up here, and the
        // box always shows the moment that was actually resolved to.
        MomentText = value.Time;
        ShowAt(value.LineNumber);
    }

    partial void OnOnlyWhatIsOnChanged(bool value)
    {
        if (SelectedLine is { } line) ShowAt(line.LineNumber);
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a MachineLog.txt",
            Filter = "Machine log (*.txt)|*.txt|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(LogPath)) ? Path.GetDirectoryName(LogPath) : null
        };

        if (dialog.ShowDialog() == true) Load(dialog.FileName);
    }

    public void Load(string path)
    {
        try
        {
            LogPath = path;
            _timeline = IoTimeline.FromFile(path);

            _allLines = _timeline.Entries.Select(e => new LogLineRow(
                e.LineNumber,
                $"{e.Time:hh\\:mm\\:ss\\.fffffff}",
                e.Category.ToString(),
                e.Tag,
                e.Description)).ToList();

            ApplyFilter();

            var outputs = _timeline.Signals.Count(s => s.Kind == SignalKind.Output);
            var inputs = _timeline.Signals.Count - outputs;

            Heading = $"{Path.GetFileName(path)} - {_timeline.Entries.Count:N0} line(s), "
                      + $"{_timeline.Changes.Count:N0} I/O change(s), {outputs} output(s) and {inputs} input(s) "
                      + $"ever move in this log.";

            Notes = BuildNotes();

            // Open on the last line, which is usually the interesting end of a support bundle.
            SelectedLine = _allLines.Count > 0 ? _allLines[^1] : null;

            StatusMessage = _timeline.Entries.Count == 0
                ? "Nothing in this file looked like a machine log line."
                : string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not read that log: {ex.Message}";
            SimpleLogger.Error("Could not read a machine log for I/O state", ex);
        }
    }

    [RelayCommand]
    private void GoToTime()
    {
        var text = MomentText.Trim();

        if (!MachineLogTime.TryParse(text, out var clock))
        {
            StatusMessage = $"\"{text}\" is not a time. Write it as 07:53:38 or 07:53:38.9085106.";
            return;
        }

        var line = _timeline.LineAt(clock);
        if (line == 0)
        {
            StatusMessage = "Nothing in this log is at or before that time.";
            return;
        }

        StatusMessage = string.Empty;
        SelectLine(line);
    }

    [RelayCommand]
    private void PreviousChange() => StepToChange(-1);

    [RelayCommand]
    private void NextChange() => StepToChange(1);

    [RelayCommand]
    private void GoToStart()
    {
        if (_allLines.Count > 0) SelectLine(_allLines[0].LineNumber);
    }

    [RelayCommand]
    private void GoToEnd()
    {
        if (_allLines.Count > 0) SelectLine(_allLines[^1].LineNumber);
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        var needle = FilterText.Trim();

        Lines = needle.Length == 0
            ? _allLines
            : _allLines.Where(l =>
                l.Tag.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || l.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || l.Category.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || l.Time.StartsWith(needle, StringComparison.OrdinalIgnoreCase)).ToList();

        StatusMessage = needle.Length == 0
            ? string.Empty
            : $"{Lines.Count:N0} of {_allLines.Count:N0} line(s) match \"{needle}\".";
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        ApplyFilter();
    }

    [RelayCommand]
    private void Copy()
    {
        try
        {
            System.Windows.Clipboard.SetText(AsText());
            StatusMessage = "Copied to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not copy: {ex.Message}";
        }
    }

    /// <summary>The snapshot as plain text, for a ticket.</summary>
    public string AsText()
    {
        var sb = new StringBuilder();

        sb.AppendLine(Heading);
        sb.AppendLine($"At {MomentText}");
        if (SelectedLine is { } line) sb.AppendLine($"Line {line.LineNumber}: {line.Tag}  {line.Description}");
        sb.AppendLine(Summary);
        sb.AppendLine();

        foreach (var (title, rows) in new[] { ("OUTPUTS", Outputs), ("INPUTS", Inputs) })
        {
            sb.AppendLine(title);

            foreach (var row in rows)
                sb.AppendLine($"  {row.State,-3} {row.Name,-32} {row.Address,-22} {row.Since,-30} "
                              + $"{row.Moves:N0} move(s) so far, {row.MovesInFile:N0} in the file"
                              + (row.ReadBackwards ? "  (read backwards)" : string.Empty));

            sb.AppendLine();
        }

        if (_quietPoints.Count > 0)
        {
            sb.AppendLine($"KNOWN ON THIS MACHINE BUT NEVER MOVING IN THIS LOG ({_quietPoints.Count})");

            foreach (var point in _quietPoints)
                sb.AppendLine($"  {point.Kind,-7} {point.Name,-32} {point.Address,-22} "
                              + $"{point.TotalChanges:N0} change(s) seen across {point.BundlesSeenIn} bundle(s)");

            sb.AppendLine();
        }

        if (Notes.Length > 0) sb.AppendLine(Notes);

        return sb.ToString();
    }

    private void StepToChange(int direction)
    {
        var from = SelectedLine?.LineNumber ?? 0;

        var next = direction > 0
            ? _timeline.Changes.FirstOrDefault(c => c.LineNumber > from)
            : _timeline.Changes.LastOrDefault(c => c.LineNumber < from);

        if (next is null)
        {
            StatusMessage = direction > 0
                ? "That is the last I/O change in this log."
                : "That is the first I/O change in this log.";
            return;
        }

        StatusMessage = string.Empty;
        SelectLine(next.LineNumber);
    }

    /// <summary>Move the selection, bringing the line back into the filtered list if it was filtered out.</summary>
    private void SelectLine(int lineNumber)
    {
        var row = Lines.FirstOrDefault(l => l.LineNumber == lineNumber);

        if (row is null)
        {
            Lines = _allLines;
            FilterText = string.Empty;
            row = _allLines.FirstOrDefault(l => l.LineNumber == lineNumber);
        }

        SelectedLine = row;
    }

    private void ShowAt(int lineNumber)
    {
        var snapshot = _timeline.AtLine(lineNumber);

        Outputs = Rows(snapshot.Outputs);
        Inputs = Rows(snapshot.Inputs);
        Summary = snapshot.Summary;
    }

    private IReadOnlyList<SignalRow> Rows(IReadOnlyList<SignalState> states) => states
        // Name order, always. Sorting by state would make rows jump about as you step through the
        // log, which is the one thing that makes a list like this unreadable.
        .Where(s => !OnlyWhatIsOn || s.On)
        .Select(s => new SignalRow(s.Id.Name, s.Id.Address, s.OnOff, s.Held,
            s.ChangesBefore, s.ChangesTotal, s.On, s.Source != StateSource.Measured))
        .ToList();

    private string BuildNotes()
    {
        var notes = new List<string>();

        if (_quietPoints.Count > 0)
        {
            var shown = _quietPoints.Take(8).Select(p => $"{p.Kind.ToLowerInvariant()} {p.Name}");

            notes.Add($"{_serialNumber} is known to have {_quietPoints.Count} other point(s) that never "
                      + $"move anywhere in this log: {string.Join(", ", shown)}"
                      + (_quietPoints.Count > 8 ? ", and more - the full list is in Copy for ticket." : ".")
                      + " Learned from this machine's earlier bundles.");
        }

        if (_timeline.Unreadable.Count > 0)
            notes.Add($"{_timeline.Unreadable.Count} I/O line(s) were worded in a way this build "
                      + "does not recognise and are not counted. Send the log in so it can be added.");

        if (_timeline.Days > 0)
            notes.Add($"This log runs past midnight {_timeline.Days} time(s). A time typed in the box "
                      + "lands on the last place it occurs - pick the line instead if you need an earlier one.");

        foreach (var shared in _timeline.SharedAddresses)
            notes.Add($"{shared.Key} carries {shared.Count()} points: "
                      + string.Join(", ", shared.Select(s => $"{s.Kind.ToString().ToLowerInvariant()} {s.Name}"))
                      + ". They are tracked separately because the log moves them separately.");

        return string.Join(Environment.NewLine, notes);
    }
}
