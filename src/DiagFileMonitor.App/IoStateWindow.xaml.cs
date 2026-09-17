using System.Windows;
using System.Windows.Controls;

namespace DiagFileMonitor.App;

public partial class IoStateWindow : Window
{
    public IoStateWindow()
    {
        InitializeComponent();

        // Press ?, then click anything to find out what it does.
        DiagFileMonitor.App.Help.HelpMode.Attach(this, HelpModeButton);
    }

    /// <summary>
    /// Keeps the chosen line in view when the selection is moved from a button rather than by
    /// clicking. Stepping change by change is useless if the row scrolls off the top.
    /// </summary>
    private void Lines_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is { } row) grid.ScrollIntoView(row);
    }
}
