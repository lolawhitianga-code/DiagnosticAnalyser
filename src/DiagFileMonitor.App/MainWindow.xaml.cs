using System.Windows;

namespace DiagFileMonitor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Press ?, then click anything to find out what it does.
        DiagFileMonitor.App.Help.HelpMode.Attach(this, HelpModeButton, say =>
        {
            if (DataContext is ViewModels.MainViewModel viewModel) viewModel.StatusMessage = say;
        });
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ViewModels.MainViewModel previous)
        {
            previous.AnalysisReady -= OnAnalysisReady;
            previous.FeedbackRequested -= OnFeedbackRequested;
            previous.ReportRequested -= OnReportRequested;
            previous.ProductionReportRequested -= OnProductionReportRequested;
            previous.IoStateRequested -= OnIoStateRequested;
        }

        if (e.NewValue is ViewModels.MainViewModel current)
        {
            current.AnalysisReady += OnAnalysisReady;
            current.FeedbackRequested += OnFeedbackRequested;
            current.ReportRequested += OnReportRequested;
            current.ProductionReportRequested += OnProductionReportRequested;
            current.IoStateRequested += OnIoStateRequested;
        }
    }

    private void OnAnalysisReady(object? sender, ViewModels.MainViewModel.AnalysisResult result)
    {
        new AnalysisWindow
        {
            Owner = this,
            DataContext = new ViewModels.AnalysisViewModel(result.Heading, result.ReportText)
        }.Show();
    }

    /// <summary>
    /// Dropping bundles on the window imports them. The shortest path there is: no folder to find,
    /// nothing to copy, no waiting for the watcher.
    /// <para>
    /// Every drag-and-drop type here is written out in full. This project builds with both WPF and
    /// WinForms switched on, and DragEventArgs, DataFormats and DragDropEffects each exist in both
    /// - so an unqualified one does not compile. It is not fussiness; it is the third time this
    /// has broken a build.
    /// </para>
    /// </summary>
    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths) return;

        e.Handled = true;
        await viewModel.ImportPathsAsync(paths);
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        // Say up front whether the drop will do anything, rather than swallowing it silently.
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;

        e.Handled = true;
    }

    private void OnIoStateRequested(object? sender, ViewModels.MainViewModel.IoStateRequestArgs request)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        var model = new ViewModels.IoStateViewModel(
            request.MachineLogPath, request.Heading, request.SerialNumber, viewModel.SignalCatalogue);

        new IoStateWindow { Owner = this, DataContext = model }.Show();

        // What else this machine is known to have is a database read, so it lands a moment after
        // the window rather than holding it shut.
        _ = model.LoadCatalogueAsync();
    }

    private void OnReportRequested(object? sender, ViewModels.MainViewModel.ReportRequestArgs request)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        new ReportWindow
        {
            Owner = this,
            DataContext = new ViewModels.ReportViewModel(
                viewModel.ReportService, request.OutputFolder, request.Serials)
        }.Show();
    }

    private void OnProductionReportRequested(object? sender, ViewModels.MainViewModel.ReportRequestArgs request)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        var window = new ProductionReportWindow
        {
            Owner = this,
            DataContext = new ViewModels.ProductionReportViewModel(
                viewModel.ProductionImportService, request.OutputFolder, request.Serials)
        };

        window.Show();
        ((ViewModels.ProductionReportViewModel)window.DataContext).RefreshStoredCommand.Execute(null);
    }

    private void OnFeedbackRequested(object? sender, ViewModels.MainViewModel.FeedbackRequest request)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        new FeedbackWindow
        {
            Owner = this,
            DataContext = new ViewModels.FeedbackViewModel(
                viewModel.FeedbackPackageService, request.File, request.ReportText, request.OutputFolder)
        }.Show();
    }

    /// <summary>WPF cannot bind SelectedItems, so the multi-selection is pushed to the ViewModel here.</summary>
    private void FilesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;
        if (sender is not System.Windows.Controls.DataGrid grid) return;

        viewModel.SetSelectedFiles(grid.SelectedItems.OfType<Core.Models.DiagnosticFileSummary>());
    }

    private void OpenIntegrationSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        new IntegrationSettingsWindow
        {
            Owner = this,
            DataContext = viewModel.CreateIntegrationSettingsViewModel()
        }.ShowDialog();
    }

    private void OpenLogSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel viewModel) return;

        new LogSearchWindow
        {
            Owner = this,
            DataContext = viewModel.CreateLogSearchViewModel()
        }.Show();
    }
}
